using System.Diagnostics;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.BankStatements;

/// <summary>
/// Orquesta la importación completa del XLS BBVA Caja de Ahorros.
///
/// PIPELINE:
///   1. XlsBankStatementReader  → string?[][] (lee celdas, abstrae NPOI)
///   2. BbvaBankStatementParser → BankStatement[] (interpreta filas)
///   3. PersistAsync            → PostgreSQL con idempotencia
///
/// IDEMPOTENCIA:
///   Consulta batch de ExternalIds existentes → inserta solo los nuevos.
///   El índice único en ExternalId actúa como red de seguridad final.
/// </summary>
public sealed class BbvaBankStatementImporter
{
    private readonly XlsBankStatementReader _reader;
    private readonly BbvaBankStatementParser _parser;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BbvaBankStatementImporter> _logger;

    public BbvaBankStatementImporter(
        XlsBankStatementReader reader,
        BbvaBankStatementParser parser,
        IServiceScopeFactory scopeFactory,
        ILogger<BbvaBankStatementImporter> logger)
    {
        _reader      = reader;
        _parser      = parser;
        _scopeFactory = scopeFactory;
        _logger      = logger;
    }

    public sealed record ImportResult(
        string FilePath,
        int Inserted,
        int Duplicates,
        int ParseErrors,
        int SkippedRows,
        IReadOnlyList<string> Diagnostics,
        TimeSpan Elapsed)
    {
        public bool HasErrors => ParseErrors > 0;

        public override string ToString() =>
            $"{Path.GetFileName(FilePath)}: " +
            $"inserted={Inserted} dup={Duplicates} " +
            $"errors={ParseErrors} skipped={SkippedRows} " +
            $"({Elapsed.TotalMilliseconds:F0}ms)";
    }

    public async Task<ImportResult> ImportAsync(
        string filePath,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Iniciando importación BBVA XLS: {File}", filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogError("Archivo no encontrado: {File}", filePath);
            return Failure(filePath, "Archivo no encontrado", sw.Elapsed);
        }

        // ── Paso 1: Leer XLS ──────────────────────────────────────
        string?[][] rawRows;
        string sheetName;
        try
        {
            (rawRows, sheetName) = _reader.ReadFirstSheet(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo XLS: {File}", filePath);
            return Failure(filePath, $"No se pudo abrir el archivo: {ex.Message}", sw.Elapsed);
        }

        // ── Paso 2: Parsear ───────────────────────────────────────
        var parseResult = _parser.Parse(rawRows, filePath, sheetName);

        if (parseResult.Statements.Count == 0)
        {
            _logger.LogWarning(
                "BBVA importer: sin movimientos parseados de {File}. Diagnostics: {Diag}",
                filePath,
                string.Join("; ", parseResult.Diagnostics.Take(5)));

            return new ImportResult(
                filePath,
                Inserted: 0, Duplicates: 0,
                ParseErrors: parseResult.Diagnostics.Count,
                SkippedRows: parseResult.SkippedRows,
                Diagnostics: parseResult.Diagnostics,
                Elapsed: sw.Elapsed);
        }

        // ── Paso 3: Persistir con idempotencia ────────────────────
        var (inserted, duplicates) = await PersistAsync(parseResult.Statements, ct);

        sw.Stop();
        var allDiagnostics = parseResult.Diagnostics.ToList();

        var result = new ImportResult(
            filePath,
            Inserted: inserted,
            Duplicates: duplicates,
            ParseErrors: parseResult.Diagnostics.Count,
            SkippedRows: parseResult.SkippedRows,
            Diagnostics: allDiagnostics.AsReadOnly(),
            Elapsed: sw.Elapsed);

        _logger.LogInformation("BBVA importer: {Result}", result);
        return result;
    }

    private async Task<(int Inserted, int Duplicates)> PersistAsync(
        IReadOnlyList<BankStatement> statements,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var incomingIds = statements.Select(s => s.ExternalId).ToHashSet();

        // Una sola query para saber qué ya existe
        var existingIds = await db.BankStatements
            .Where(s => incomingIds.Contains(s.ExternalId))
            .Select(s => s.ExternalId)
            .ToHashSetAsync(ct);

        var toInsert   = statements.Where(s => !existingIds.Contains(s.ExternalId)).ToList();
        var duplicates = statements.Count - toInsert.Count;

        if (duplicates > 0)
            _logger.LogInformation(
                "BBVA importer: {Count} movimientos ya existían (idempotencia)",
                duplicates);

        if (toInsert.Count == 0)
            return (0, duplicates);

        db.BankStatements.AddRange(toInsert);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("BBVA importer: {Count} movimientos persistidos", toInsert.Count);
        return (toInsert.Count, duplicates);
    }

    private static ImportResult Failure(string filePath, string error, TimeSpan elapsed) =>
        new(filePath,
            Inserted: 0, Duplicates: 0, ParseErrors: 1, SkippedRows: 0,
            Diagnostics: [error],
            Elapsed: elapsed);
}
