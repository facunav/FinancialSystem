using System.Diagnostics;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Helpers;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
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

        // [DIAG-FA] Instrumentación temporal — remover al cerrar el diagnóstico.
        var diagAccountNumbers = normalized.Select(p => p.AccountNumber).Distinct().ToList();
        logger.LogWarning(
            "[DIAG-FA] (2) ParsedTransaction.AccountNumber distintos en {FilePath}: [{Values}]",
            filePath,
            string.Join(", ", diagAccountNumbers.Select(v => $"'{v ?? "<null>"}'")));

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<(ParsedTransaction Parsed, string ExternalId)>();
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

            candidates.Add((parsed, externalId));
        }

        // Idempotencia entre corridas: una sola query batch para saber qué ExternalId ya
        // existen (mismo patrón que BbvaBankStatementImporter.PersistAsync) — sin esto,
        // reimportar un archivo ya importado chocaba contra el índice único de
        // Transactions.ExternalId sin manejo, y perdía también las filas nuevas del mismo
        // archivo (SaveChangesAsync es una sola transacción implícita).
        var incomingIds = candidates.Select(c => c.ExternalId).ToHashSet();
        var existingIds = await db.Transactions
            .Where(t => incomingIds.Contains(t.ExternalId))
            .Select(t => t.ExternalId)
            .ToHashSetAsync(cancellationToken);

        // Cuenta financiera: mismo patrón que BbvaBankStatementImporter.AssignFinancialAccountAsync
        // -- una sola query batch, porque el número de cuenta es el mismo para todo el
        // archivo (viene del encabezado del PDF, no por transacción). A diferencia de
        // BankStatement, Transaction no persiste su propio AccountNumber, así que acá se
        // resuelve el FinancialAccountId antes de construir cada fila, no después.
        var financialAccountId = await ResolveFinancialAccountIdAsync(db, candidates, cancellationToken);

        // [DIAG-FA] Instrumentación temporal — remover al cerrar el diagnóstico.
        logger.LogWarning(
            "[DIAG-FA] (7) financialAccountId a asignar en este HandleFileAsync: {FinancialAccountId}",
            financialAccountId?.ToString() ?? "<null>");

        var inserted = 0;
        foreach (var (parsed, externalId) in candidates)
        {
            if (existingIds.Contains(externalId))
            {
                duplicates++;
                logger.LogWarning(
                    "[DIAG-FA] Fila con ExternalId={ExternalId} ya existe en Transactions -- se saltea sin tocar su FinancialAccountId",
                    externalId);
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
                ExternalId = externalId,
                FinancialAccountId = financialAccountId
            });
            inserted++;

            logger.LogWarning(
                "[DIAG-FA] (7) Transaction insertada ExternalId={ExternalId} -> FinancialAccountId={FinancialAccountId}",
                externalId, financialAccountId?.ToString() ?? "<null>");
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

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

    /// <summary>
    /// Resuelve FinancialAccountId cuando el número de cuenta extraído del encabezado
    /// del PDF coincide, de forma exacta y sin ambigüedad, con una única FinancialAccount
    /// activa. Si no hay número de cuenta, no hay match, o hay más de una cuenta con el
    /// mismo número, no asigna nada -- la transacción queda igual que hoy (sin cuenta).
    /// Mismo patrón que BbvaBankStatementImporter.AssignFinancialAccountAsync.
    /// </summary>
    private async Task<Guid?> ResolveFinancialAccountIdAsync(
        IApplicationDbContext db,
        IReadOnlyList<(ParsedTransaction Parsed, string ExternalId)> candidates,
        CancellationToken ct)
    {
        // [DIAG-FA] Instrumentación temporal — remover al cerrar el diagnóstico.
        logger.LogWarning(
            "[DIAG-FA] ResolveFinancialAccountIdAsync invocado — candidates.Count={Count}",
            candidates.Count);

        var accountNumber = candidates
            .Select(c => c.Parsed.AccountNumber)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        // [DIAG-FA] (3) valor que llega acá.
        logger.LogWarning(
            "[DIAG-FA] (3) accountNumber resuelto de candidates: '{AccountNumber}'",
            accountNumber ?? "<null>");

        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            logger.LogWarning("[DIAG-FA] accountNumber vacío/null — return null sin consultar FinancialAccounts");
            return null;
        }

        var query = db.FinancialAccounts
            .Where(a => !a.IsDeactivated && a.AccountNumber == accountNumber)
            .Select(a => a.Id);

        // [DIAG-FA] (4) SQL/LINQ exacto que se va a ejecutar.
        logger.LogWarning("[DIAG-FA] (4) Query: {Sql}", query.ToQueryString());

        var matches = await query.ToListAsync(ct);

        // [DIAG-FA] (5) cuántas filas devolvió.
        logger.LogWarning(
            "[DIAG-FA] (5) matches.Count={Count} para accountNumber='{AccountNumber}'",
            matches.Count, accountNumber);

        if (matches.Count != 1)
        {
            if (matches.Count > 1)
                logger.LogWarning(
                    "Transaction importer: {Count} cuentas financieras activas coinciden con el número " +
                    "'{AccountNumber}' — no se asigna automáticamente (ambiguo)",
                    matches.Count, accountNumber);
            return null;
        }

        // [DIAG-FA] (6) Id encontrado.
        logger.LogWarning("[DIAG-FA] (6) Id encontrado: {FinancialAccountId}", matches[0]);

        logger.LogInformation(
            "Transaction importer: cuenta financiera {FinancialAccountId} asignada automáticamente " +
            "(número '{AccountNumber}')",
            matches[0], accountNumber);

        return matches[0];
    }
}
