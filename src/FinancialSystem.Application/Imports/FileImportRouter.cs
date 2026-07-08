using System.Security.Cryptography;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Enruta cada archivo al primer handler que lo acepta.
///
/// ALGORITMO:
///   Itera los handlers en el orden en que fueron registrados en DI.
///   El primer CanHandle() = true gana. Si ninguno acepta, loguea warning.
///
/// El orden de registro en DI es la única forma de controlar prioridad.
/// Los handlers más específicos deben registrarse primero.
///
/// PERSISTENCIA DE ImportBatch (PR I4):
///   Este es el único punto del pipeline por el que pasa toda importación, sin importar
///   la fuente — por eso la persistencia de ImportBatch se centraliza acá y no en cada
///   handler/importador. Un handler nuevo (IFileImportHandler) obtiene registro de
///   ImportBatch automáticamente con solo devolver un ImportRunResult, sin conocer
///   ImportBatch en absoluto.
/// </summary>
public class FileImportRouter : IFileImportRouter
{
    private readonly IReadOnlyList<IFileImportHandler> _handlers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<FileImportRouter> _logger;

    public FileImportRouter(
        IEnumerable<IFileImportHandler> handlers,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<FileImportRouter> logger)
    {
        _handlers = handlers.ToList().AsReadOnly();
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        _logger.LogInformation(
            "FileImportRouter: {Count} handlers registrados en orden: [{Handlers}]",
            _handlers.Count,
            string.Join(" → ", _handlers.Select(h => h.HandlerName)));
    }

    public async Task RouteAsync(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(filePath))
                continue;

            _logger.LogInformation(
                "Router: '{File}' → [{Handler}]",
                fileName,
                handler.HandlerName);

            var startedAtUtc = _dateTimeProvider.UtcNow;
            ImportRunResult result;
            try
            {
                result = await handler.HandleAsync(filePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Router: el handler [{Handler}] lanzó una excepción no controlada procesando '{File}'",
                    handler.HandlerName, fileName);
                result = ImportRunResult.Failure($"Excepción no controlada: {ex.Message}");
            }

            await PersistImportBatchAsync(filePath, handler.HandlerName, startedAtUtc, result, ct);
            return;
        }

        _logger.LogWarning(
            "Router: ningún handler aceptó '{File}'. " +
            "Handlers disponibles: [{Handlers}]",
            fileName,
            string.Join(", ", _handlers.Select(h => h.HandlerName)));
    }

    /// <summary>
    /// Persiste el ImportBatch (+ sus ImportBatchLine) de esta corrida en un único
    /// SaveChangesAsync — atómico respecto a sí mismo, pero en una transacción separada
    /// de la que ya usó el handler para persistir sus datos (ver justificación en el
    /// resumen de PR I4: ImportBatch es un registro de auditoría, no la fuente de verdad
    /// financiera). Si esto falla, se loguea pero no se relanza — los datos del handler
    /// ya quedaron guardados y no tiene sentido tratar la corrida completa como fallida
    /// por un problema al escribir el registro de auditoría.
    /// </summary>
    private async Task PersistImportBatchAsync(
        string filePath,
        string handlerName,
        DateTime startedAtUtc,
        ImportRunResult result,
        CancellationToken ct)
    {
        var batch = new ImportBatch
        {
            SourceFile = filePath,
            ContentHash = TryComputeContentHash(filePath),
            HandlerName = handlerName,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = _dateTimeProvider.UtcNow,
            InsertedCount = result.Inserted,
            DuplicateCount = result.Duplicates,
            FailedCount = result.Failed,
            SkippedCount = result.Skipped
        };

        var lineNumber = 0;
        foreach (var diagnostic in result.Diagnostics)
        {
            batch.Lines.Add(new ImportBatchLine
            {
                LineNumber = ++lineNumber,
                RawText = diagnostic,
                Reason = diagnostic
            });
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Router: no se pudo persistir el ImportBatch de '{File}' (handler {Handler})",
                Path.GetFileName(filePath), handlerName);
        }
    }

    private string TryComputeContentHash(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router: no se pudo calcular ContentHash para '{File}'", filePath);
            return string.Empty;
        }
    }
}
