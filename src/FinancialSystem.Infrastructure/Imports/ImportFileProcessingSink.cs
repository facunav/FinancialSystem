using System.Diagnostics;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Helpers;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports;

internal sealed class ImportFileProcessingSink(
    IFileParserFactory parserFactory,
    ITransactionNormalizer normalizer,
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider dateTimeProvider,
    ILogger<ImportFileProcessingSink> logger) : IImportFileSink
{
    public async Task<ImportRunResult> HandleFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var totalSw = Stopwatch.StartNew();
        logger.LogInformation("Import file detected: {FilePath}", filePath);

        if (!parserFactory.TryGetParser(filePath, out var parser) || parser is null)
        {
            logger.LogWarning(
                "No parser registered for {FilePath} (extension {Extension})",
                filePath,
                Path.GetExtension(filePath));
            return ImportRunResult.Failure(
                $"No hay parser registrado para la extensión '{Path.GetExtension(filePath)}'.");
        }

        logger.LogInformation(
            "Selected parser {ParserType} for {FilePath}",
            parser.GetType().Name,
            filePath);

        FileParseResult parseResult;
        try
        {
            parseResult = await parser.ParseAsync(filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse import file {FilePath}", filePath);
            return ImportRunResult.Failure($"Excepción parseando el archivo: {ex.Message}");
        }

        logger.LogInformation(
            "Parser {ParserType} extracted {ExtractedCount} raw transactions from {FilePath} in {ParseMs}ms ({SkippedRows} rows/lines skipped, {DiagnosticCount} diagnostics)",
            parser.GetType().Name,
            parseResult.Transactions.Count,
            filePath,
            parseResult.Elapsed.TotalMilliseconds,
            parseResult.SkippedRows,
            parseResult.Diagnostics.Count);

        foreach (var diagnostic in parseResult.Diagnostics.Take(20))
        {
            logger.LogDebug("Parse diagnostic for {FilePath}: {Diagnostic}", filePath, diagnostic);
        }

        if (parseResult.Diagnostics.Count > 20)
        {
            logger.LogDebug(
                "Parse diagnostic for {FilePath}: ... and {More} more",
                filePath,
                parseResult.Diagnostics.Count - 20);
        }

        if (parseResult.Transactions.Count == 0)
        {
            logger.LogWarning("No transactions extracted from {FilePath}", filePath);
            return new ImportRunResult(
                0, 0, parseResult.Diagnostics.Count, parseResult.SkippedRows, parseResult.Diagnostics);
        }

        var normalized = normalizer.NormalizeAll(parseResult.Transactions);
        logger.LogInformation(
            "Normalized {Count} transactions from {FilePath}",
            normalized.Count,
            filePath);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inserted = 0;
        var duplicates = 0;

        foreach (var parsed in normalized)
        {
            // ExternalId es la única fuente de verdad sobre la identidad de una transacción
            // (ver SheetParserHelpers.BuildTransactionExternalId) — el mismo valor que se
            // persiste en la columna con índice único se usa acá para dedupear dentro del
            // archivo, para que ambas nociones de "duplicado" no puedan volver a divergir.
            var externalId = SheetParserHelpers.BuildTransactionExternalId(
                parsed.Date, parsed.Amount, parsed.Description, parsed.CouponNumber);

            if (!seen.Add(externalId))
            {
                duplicates++;
                continue;
            }

            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                Date = parsed.Date,
                Description = parsed.Description,
                Amount = parsed.Amount,
                Currency = string.IsNullOrWhiteSpace(parsed.Currency) ? "ARS" : parsed.Currency,
                CreatedAtUtc = dateTimeProvider.UtcNow,
                CouponNumber = parsed.CouponNumber,
                RawLine = parsed.RawLine,
                SourceFile = filePath,
                ExternalId = externalId
            });
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        totalSw.Stop();

        logger.LogInformation(
            "Import complete for {FilePath}: {Inserted} saved, {Duplicates} duplicates in file, total time {TotalMs}ms",
            filePath,
            inserted,
            duplicates,
            totalSw.ElapsedMilliseconds);

        return new ImportRunResult(
            inserted, duplicates, parseResult.Diagnostics.Count, parseResult.SkippedRows, parseResult.Diagnostics);
    }
}
