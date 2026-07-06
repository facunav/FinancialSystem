using ClosedXML.Excel;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FinancialSystem.Infrastructure.Imports.Legacy;

/// <summary>
/// Importa registros desde el Excel histórico del usuario.
/// Solo para migración/compatibilidad — no parte del flujo principal futuro.
///
/// IDEMPOTENCIA: cada LegacyImportedExpense tiene ExternalId único (SHA256).
/// Re-importar el mismo archivo es siempre seguro.
/// </summary>
public sealed class ExcelLegacyExpenseImporter : ILegacyExpenseImporter
{
    private readonly IReadOnlyList<ILegacyExpenseSheetParser> _sheetParsers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExcelLegacyExpenseImporter> _logger;

    public ExcelLegacyExpenseImporter(
        IEnumerable<ILegacyExpenseSheetParser> sheetParsers,
        IServiceScopeFactory scopeFactory,
        ILogger<ExcelLegacyExpenseImporter> logger)
    {
        _sheetParsers = sheetParsers.ToList().AsReadOnly();
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<LegacyExpenseImportResult> ImportAsync(
        string filePath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Importando Excel legacy: {FilePath}", filePath);

        if (!File.Exists(filePath))
            return Failure(filePath, $"Archivo no encontrado: {filePath}", sw.Elapsed);

        List<LegacyImportedExpense> allExpenses;
        var diagnostics = new List<string>();
        int totalSkipped, totalErrors;

        try
        {
            (allExpenses, totalSkipped, totalErrors) = ParseWorkbook(filePath, diagnostics, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error abriendo el workbook {FilePath}", filePath);
            return Failure(filePath, $"No se pudo abrir el archivo: {ex.Message}", sw.Elapsed);
        }

        _logger.LogInformation(
            "Parsing completo: {Total} registros extraídos ({Skipped} omitidas, {Errors} errores)",
            allExpenses.Count, totalSkipped, totalErrors);

        if (allExpenses.Count == 0)
            return new LegacyExpenseImportResult
            {
                FilePath = filePath,
                Inserted = 0,
                Skipped = totalSkipped,
                Duplicates = 0,
                ParseErrors = totalErrors,
                Diagnostics = diagnostics,
                Elapsed = sw.Elapsed,
            };

        var (inserted, duplicates) = await PersistAsync(allExpenses, diagnostics, ct);
        sw.Stop();

        var result = new LegacyExpenseImportResult
        {
            FilePath = filePath,
            Inserted = inserted,
            Skipped = totalSkipped,
            Duplicates = duplicates,
            ParseErrors = totalErrors,
            Diagnostics = diagnostics.AsReadOnly(),
            Elapsed = sw.Elapsed,
        };
        _logger.LogInformation("Importación completa: {Result}", result);
        return result;
    }

    private (List<LegacyImportedExpense>, int, int) ParseWorkbook(
        string filePath, List<string> diagnostics, CancellationToken ct)
    {
        var all = new List<LegacyImportedExpense>();
        var skipped = 0;

        using var cleanStream = XlsxSanitizer.StripDataValidations(filePath, _logger);
        using var workbook = new XLWorkbook(cleanStream);

        foreach (var ws in workbook.Worksheets)
        {
            ct.ThrowIfCancellationRequested();
            if (ws.Visibility != XLWorksheetVisibility.Visible) continue;

            var parser = _sheetParsers.FirstOrDefault(p =>
                p.HandledSheetNames.Any(n =>
                    string.Equals(n, ws.Name, StringComparison.OrdinalIgnoreCase)));

            if (parser is null)
            {
                diagnostics.Add($"Hoja '{ws.Name}': sin parser → ignorada");
                continue;
            }

            var reader = new ClosedXmlSheetReader(ws);
            var result = parser.Parse(reader, filePath);
            all.AddRange(result.Expenses);
            skipped += result.SkippedRows;
            diagnostics.AddRange(result.Diagnostics);
        }

        return (all, skipped, 0);
    }

    private async Task<(int Inserted, int Duplicates)> PersistAsync(
        List<LegacyImportedExpense> expenses,
        List<string> diagnostics,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var incomingIds = expenses.Select(e => e.ExternalId).ToHashSet();
        var existingIds = await db.LegacyImportedExpenses
            .Where(e => incomingIds.Contains(e.ExternalId))
            .Select(e => e.ExternalId)
            .ToHashSetAsync(ct);

        var toInsert = expenses.Where(e => !existingIds.Contains(e.ExternalId)).ToList();
        var duplicates = expenses.Count - toInsert.Count;

        if (duplicates > 0)
            _logger.LogInformation("{Dup} registros ya existían (idempotencia)", duplicates);

        if (toInsert.Count == 0) return (0, duplicates);

        db.LegacyImportedExpenses.AddRange(toInsert);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("{Count} registros legacy persistidos", toInsert.Count);
        return (toInsert.Count, duplicates);
    }

    private static LegacyExpenseImportResult Failure(string path, string error, TimeSpan elapsed) =>
        new()
        {
            FilePath = path,
            Inserted = 0,
            Skipped = 0,
            Duplicates = 0,
            ParseErrors = 1,
            Diagnostics = [error],
            Elapsed = elapsed
        };
}

// ── ClosedXML adapter interno ─────────────────────────────────────────────────

internal sealed class ClosedXmlSheetReader : ISheetReader
{
    private readonly IXLWorksheet _ws;
    public ClosedXmlSheetReader(IXLWorksheet ws) => _ws = ws;
    public string SheetName => _ws.Name;
    public int RowCount => _ws.RangeUsed()?.LastRow().RowNumber() ?? 0;

    public string? GetString(int row, int col)
    {
        var cell = _ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        var v = cell.GetFormattedString()?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public DateTime? GetDate(int row, int col)
    {
        var cell = _ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue(out DateTime dt)) return dt;
        var str = cell.GetFormattedString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return null;
        return ImportValueParser.TryParseDate(str, out var parsed) ? parsed : null;
    }

    public decimal? GetDecimal(int row, int col)
    {
        var cell = _ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue(out decimal d)) return d;
        if (cell.TryGetValue(out double dbl)) return (decimal)dbl;
        var str = cell.GetFormattedString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return null;
        return ImportValueParser.TryParseAmount(str, out var amount) ? amount : null;
    }
}