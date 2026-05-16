using System.Collections.Concurrent;
using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Worker.Services;

public sealed class ImportsFolderWatcherHostedService(
    IImportFileSink importFileSink,
    IOptions<FileIngestionOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<ImportsFolderWatcherHostedService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var importsPath = ResolveImportsPath();
        Directory.CreateDirectory(importsPath);

        logger.LogInformation(
            "Watching import folder {ImportsPath} for {Extensions}",
            importsPath,
            string.Join(", ", FileIngestionOptions.WatchedExtensions));

        using var watcher = new FileSystemWatcher(importsPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        void OnFileEvent(object _, FileSystemEventArgs e) =>
            ScheduleProcessing(e.FullPath, stoppingToken);

        watcher.Created += OnFileEvent;
        watcher.Changed += OnFileEvent;
        watcher.Renamed += (_, e) => ScheduleProcessing(e.FullPath, stoppingToken);

        foreach (var existing in EnumerateWatchedFiles(importsPath))
        {
            ScheduleProcessing(existing, stoppingToken);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        finally
        {
            foreach (var cts in _pending.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pending.Clear();
        }
    }

    private void ScheduleProcessing(string filePath, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var extension = Path.GetExtension(filePath);
        if (!FileIngestionOptions.WatchedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Archivo ignorado (extensión no soportada {Extension}): {FilePath}. " +
                "Extensiones soportadas: .csv, .pdf, .xlsx",
                extension,
                filePath);
            return;
        }

        var debounceMs = Math.Max(0, options.Value.DebounceMilliseconds);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var cts = _pending.AddOrUpdate(
            filePath,
            _ => linkedCts,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return linkedCts;
            });

        if (cts != linkedCts)
        {
            linkedCts.Dispose();
        }

        _ = ProcessAfterDebounceAsync(filePath, debounceMs, cts);
    }

    private async Task ProcessAfterDebounceAsync(string filePath, int debounceMs, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(debounceMs, cts.Token);
            if (!File.Exists(filePath))
            {
                return;
            }

            await WaitForFileReadyAsync(filePath, cts.Token);
            await importFileSink.HandleFileAsync(filePath, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer event or shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process import file {FilePath}", filePath);
        }
        finally
        {
            if (_pending.TryGetValue(filePath, out var current) && ReferenceEquals(current, cts))
            {
                _pending.TryRemove(filePath, out _);
            }

            cts.Dispose();
        }
    }

    private static async Task WaitForFileReadyAsync(string filePath, CancellationToken cancellationToken)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
                return;
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private string ResolveImportsPath()
    {
        var configured = options.Value.ImportsPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(AppContext.BaseDirectory, "imports");
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(hostEnvironment.ContentRootPath, configured);
    }

    private static IEnumerable<string> EnumerateWatchedFiles(string importsPath)
    {
        if (!Directory.Exists(importsPath))
        {
            yield break;
        }

        foreach (var extension in FileIngestionOptions.WatchedExtensions)
        {
            foreach (var file in Directory.EnumerateFiles(importsPath, $"*{extension}"))
            {
                yield return file;
            }
        }
    }
}
