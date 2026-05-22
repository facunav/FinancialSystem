using ClosedXML.Excel;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FinancialSystem.Infrastructure.Imports.ManualExpenses;

/// <summary>
/// Importador completo del Excel manual financiero.
///
/// RESPONSABILIDADES:
///   1. Abrir el workbook con ClosedXML
///   2. Para cada hoja visible, encontrar el parser adecuado via IManualExpenseSheetParser
///   3. Parsear filas → ManualExpense[]
///   4. Persistir con idempotencia real contra PostgreSQL
///   5. Devolver un resultado detallado con métricas y diagnósticos
///
/// IDEMPOTENCIA (estrategia "upsert-ignore"):
///   Cada ManualExpense tiene un ExternalId único (SHA256 del path+hoja+fila).
///   Antes del insert, consultamos qué ExternalIds ya existen en la DB.
///   Los que ya existen → skipped (no update, no error).
///   Los nuevos → bulk insert en una sola operación.
///   Así re-importar el mismo archivo es siempre seguro y eficiente.
///
/// SEPARACIÓN:
///   El importer NO implementa IFileParser ni IImportFileSink.
///   Es invocado directamente por el Worker cuando detecta un Excel manual,
///   separado del pipeline genérico de transacciones.
/// </summary>
public sealed class ExcelManualExpenseImporter : IManualExpenseImporter
{
    private readonly IReadOnlyList<IManualExpenseSheetParser> _sheetParsers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExcelManualExpenseImporter> _logger;

    public ExcelManualExpenseImporter(
        IEnumerable<IManualExpenseSheetParser> sheetParsers,
        IServiceScopeFactory scopeFactory,
        ILogger<ExcelManualExpenseImporter> logger)
    {
        // Construir índice de lookup: sheetName (lower) → parser
        // Un parser puede manejar múltiples nombres de hoja (variantes con/sin tilde)
        _sheetParsers = sheetParsers.ToList().AsReadOnly();
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ManualExpenseImportResult> ImportAsync(
        string filePath,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Iniciando importación manual: {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogError("Archivo no encontrado: {FilePath}", filePath);
            return Failure(filePath, $"Archivo no encontrado: {filePath}", sw.Elapsed);
        }

        // ── Paso 1: Parsear todas las hojas ──────────────────────
        List<ManualExpense> allExpenses;
        var allDiagnostics = new List<string>();
        var totalSkipped = 0;
        var totalParseErrors = 0;

        try
        {
            (allExpenses, totalSkipped, totalParseErrors, var parseDiags) =
                ParseWorkbook(filePath, allDiagnostics, ct);
            allDiagnostics.AddRange(parseDiags);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error abriendo el workbook {FilePath}", filePath);
            return Failure(filePath, $"No se pudo abrir el archivo: {ex.Message}", sw.Elapsed);
        }

        _logger.LogInformation(
            "Parsing completo: {Total} gastos extraídos de {FilePath} ({Skipped} filas omitidas, {Errors} errores)",
            allExpenses.Count, filePath, totalSkipped, totalParseErrors);

        if (allExpenses.Count == 0)
        {
            _logger.LogWarning("Sin gastos parseados de {FilePath}", filePath);
            return new ManualExpenseImportResult
            {
                FilePath = filePath, Inserted = 0, Skipped = totalSkipped,
                Duplicates = 0, ParseErrors = totalParseErrors,
                Diagnostics = allDiagnostics, Elapsed = sw.Elapsed,
            };
        }

        // ── Paso 2: Persistir con idempotencia ───────────────────
        var (inserted, duplicates) = await PersistAsync(allExpenses, allDiagnostics, ct);

        sw.Stop();
        var result = new ManualExpenseImportResult
        {
            FilePath = filePath,
            Inserted = inserted,
            Skipped = totalSkipped,
            Duplicates = duplicates,
            ParseErrors = totalParseErrors,
            Diagnostics = allDiagnostics.AsReadOnly(),
            Elapsed = sw.Elapsed,
        };

        _logger.LogInformation("Importación completa: {Result}", result);
        return result;
    }

    // ── Parsing del workbook ──────────────────────────────────────

    private (List<ManualExpense> Expenses, int Skipped, int ParseErrors, List<string> Diagnostics)
        ParseWorkbook(string filePath, List<string> diagnostics, CancellationToken ct)
    {
        var allExpenses = new List<ManualExpense>();
        var totalSkipped = 0;
        var totalErrors = 0;
        using var cleanStream = XlsxSanitizer.StripDataValidations(filePath, _logger);
        using var workbook = new XLWorkbook(cleanStream);

        foreach (var worksheet in workbook.Worksheets)
        {
            ct.ThrowIfCancellationRequested();

            if (worksheet.Visibility != XLWorksheetVisibility.Visible)
            {
                _logger.LogDebug("Hoja '{Sheet}' oculta — ignorada", worksheet.Name);
                continue;
            }

            var parser = FindParser(worksheet.Name);
            if (parser is null)
            {
                _logger.LogDebug(
                    "Hoja '{Sheet}' sin parser registrado — ignorada. Parsers: {Parsers}",
                    worksheet.Name,
                    string.Join(", ", _sheetParsers.SelectMany(p => p.HandledSheetNames)));
                diagnostics.Add($"Hoja '{worksheet.Name}': sin parser — ignorada");
                continue;
            }

            _logger.LogDebug("Procesando hoja '{Sheet}' con {Parser}",
                worksheet.Name, parser.GetType().Name);

            var reader = new ClosedXmlSheetReader(worksheet);
            var sheetResult = parser.Parse(reader, filePath);

            allExpenses.AddRange(sheetResult.Expenses);
            totalSkipped += sheetResult.SkippedRows;
            diagnostics.AddRange(sheetResult.Diagnostics);
        }

        return (allExpenses, totalSkipped, totalErrors, diagnostics);
    }

    private IManualExpenseSheetParser? FindParser(string sheetName) =>
        _sheetParsers.FirstOrDefault(p =>
            p.HandledSheetNames.Any(n =>
                string.Equals(n, sheetName, StringComparison.OrdinalIgnoreCase)));

    // ── Persistencia con idempotencia ─────────────────────────────

    private async Task<(int Inserted, int Duplicates)> PersistAsync(
        List<ManualExpense> expenses,
        List<string> diagnostics,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Obtener todos los ExternalIds del lote actual
        var incomingIds = expenses.Select(e => e.ExternalId).ToHashSet();

        // Consulta batch: qué ExternalIds ya existen en la DB
        // Una sola query en lugar de N queries individuales
        var existingIds = await db.ManualExpenses
            .Where(e => incomingIds.Contains(e.ExternalId))
            .Select(e => e.ExternalId)
            .ToHashSetAsync(ct);

        var toInsert = expenses.Where(e => !existingIds.Contains(e.ExternalId)).ToList();
        var duplicates = expenses.Count - toInsert.Count;

        if (duplicates > 0)
        {
            _logger.LogInformation(
                "{Duplicates} gastos ya existían en la DB (idempotencia) — skipped",
                duplicates);
        }

        if (toInsert.Count == 0)
            return (0, duplicates);

        // Bulk insert de los registros nuevos
        db.ManualExpenses.AddRange(toInsert);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("{Count} gastos manuales persistidos", toInsert.Count);
        return (toInsert.Count, duplicates);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static ManualExpenseImportResult Failure(
        string filePath, string error, TimeSpan elapsed) =>
        new()
        {
            FilePath = filePath,
            Inserted = 0, Skipped = 0, Duplicates = 0, ParseErrors = 1,
            Diagnostics = [error],
            Elapsed = elapsed,
        };
}
