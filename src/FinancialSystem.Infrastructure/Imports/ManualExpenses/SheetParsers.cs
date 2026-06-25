using ClosedXML.Excel;
using FinancialSystem.Application.Helpers;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.ManualExpenses;

// ════════════════════════════════════════════════════════════════
// CLOSEDXML ADAPTER
// Implementa ISheetReader sobre IXLWorksheet.
// Aísla ClosedXML del dominio de parsing → fácil de mockear en tests.
// ════════════════════════════════════════════════════════════════

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
        var value = cell.GetFormattedString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public DateTime? GetDate(int row, int col)
    {
        var cell = _ws.Cell(row, col);
        if (cell.IsEmpty()) return null;

        // ClosedXML a veces almacena fechas como DateTime directamente
        if (cell.TryGetValue(out DateTime dt)) return dt;

        // Fallback: parsear desde string formateado
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

// ════════════════════════════════════════════════════════════════
// PARSER DE GASTOS DINÁMICOS
//
// Columnas del Excel (índices base-1):
//   1: Fecha | 2: Categoria | 3: Forma de Pago | 4: Monto | 5: Comentario
//
// Particularidades conocidas:
//   - Comentario es opcional (165 de 596 filas lo tienen vacío)
//   - Typo "Creidito" → se normaliza a "Credito"
//   - Filas vacías al final del rango usado → se skipean
// ════════════════════════════════════════════════════════════════

public sealed class DynamicSheetParser : IManualExpenseSheetParser
{
    private const int ColFecha      = 1;
    private const int ColCategoria  = 2;
    private const int ColPago       = 3;
    private const int ColMonto      = 4;
    private const int ColComentario = 5;
    private const int HeaderRow     = 1;

    private readonly ILogger<DynamicSheetParser> _logger;

    public DynamicSheetParser(ILogger<DynamicSheetParser> logger) => _logger = logger;

    public IReadOnlyList<string> HandledSheetNames { get; } =
        ["Gastos Dinamicos", "Gastos Dinámicos", "GastosDinamicos", "Dinamicos"];

    public SheetParseResult Parse(ISheetReader sheet, string sourceFile)
    {
        var expenses   = new List<ManualExpense>();
        var diagnostics = new List<string>();
        var skipped    = 0;

        for (var row = HeaderRow + 1; row <= sheet.RowCount; row++)
        {
            // Fila completamente vacía → fin de datos o fila separadora
            var dateRaw = sheet.GetString(row, ColFecha);
            var catRaw  = sheet.GetString(row, ColCategoria);
            var amount  = sheet.GetDecimal(row, ColMonto);

            if (dateRaw is null && catRaw is null && amount is null)
            {
                skipped++;
                continue;
            }

            // ── Fecha ────────────────────────────────────────────
            var date = sheet.GetDate(row, ColFecha);
            if (date is null)
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: fecha inválida '{dateRaw}' — omitida");
                _logger.LogDebug("GastosDinamicos fila {Row}: fecha inválida '{Raw}'", row, dateRaw);
                skipped++;
                continue;
            }

            // ── Categoría ────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(catRaw))
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: categoría vacía — omitida");
                skipped++;
                continue;
            }

            // ── Monto ─────────────────────────────────────────────
            if (amount is null)
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: monto inválido — omitida");
                skipped++;
                continue;
            }

            // ── Forma de pago (con corrección de typo) ────────────
            var paymentRaw = sheet.GetString(row, ColPago) ?? string.Empty;
            var payment    = CommonHelper.NormalizePaymentMethod(paymentRaw);

            // ── Comentario (opcional) ─────────────────────────────
            var notes = sheet.GetString(row, ColComentario);

            expenses.Add(new ManualExpense
            {
                Date          = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc),
                Description      = catRaw.Trim(),
                PaymentMethod = payment,
                Amount        = amount.Value,
                Currency      = "ARS",
                Notes         = notes,
                Sheet         = ManualExpenseSheet.Dynamic,
                ExternalId    = CommonHelper.BuildExternalId(sourceFile, sheet.SheetName, row),
                SourceFile    = sourceFile,
                SheetName     = sheet.SheetName,
                RowNumber     = row,
                ImportedAtUtc = DateTime.UtcNow,
            });
        }

        _logger.LogInformation(
            "[{Sheet}] {Count} gastos parseados, {Skipped} filas omitidas",
            sheet.SheetName, expenses.Count, skipped);

        return new SheetParseResult(expenses, skipped, diagnostics);
    }
}
