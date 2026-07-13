using System.Globalization;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.BankStatements;

/// <summary>
/// Parsea las filas ya leídas del XLSX "Últimos Movimientos" de Tarjeta de Débito BBVA.
///
/// ESTRUCTURA CONFIRMADA CON EL ARCHIVO REAL:
///   Fila 0 (base-0): vacía
///   Fila 1:          "Últimos Movimientos Visa Débito"
///   Fila 2:          "Fecha | Movimientos | Importe"
///   Fila 3+:         Datos de operaciones
///
/// COLUMNAS (base-0):
///   0 = Fecha       → ISO 8601 con offset, ej. "2026-07-07T18:57:38.000-0300"
///   1 = Movimientos → comercio, ej. "OPENPAY*CAMPO VERDE"
///   2 = Importe     → ej. "ARS39886.67" (siempre positivo, prefijo de moneda)
///
/// RESPONSABILIDAD: solo parsear → DebitCardOperation[]. No conoce NPOI, EF Core,
/// ni el matching contra BankStatement (eso es de BbvaDebitCardEnrichmentHandler).
/// </summary>
public sealed class BbvaDebitCardParser
{
    private const int HeaderRowIdx = 2;
    private const int DataStartIdx = 3;

    private const int ColFecha = 0;
    private const int ColMovimiento = 1;
    private const int ColImporte = 2;

    private readonly ILogger<BbvaDebitCardParser> _logger;

    public BbvaDebitCardParser(ILogger<BbvaDebitCardParser> logger) => _logger = logger;

    public sealed record DebitCardOperation(DateTimeOffset PurchaseAt, string Merchant, decimal Amount);

    public sealed record ParseResult(
        IReadOnlyList<DebitCardOperation> Operations,
        int SkippedRows,
        IReadOnlyList<string> Diagnostics);

    public ParseResult Parse(IReadOnlyList<string?[]> rows, string sourceFile)
    {
        var operations = new List<DebitCardOperation>();
        var diagnostics = new List<string>();
        var skipped = 0;

        if (rows.Count < DataStartIdx + 1)
        {
            diagnostics.Add("Archivo sin filas de datos");
            return new ParseResult(operations, 0, diagnostics);
        }

        ValidateHeader(rows[HeaderRowIdx], diagnostics);

        for (var i = DataStartIdx; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 1; // 1-based para trazabilidad

            if (IsEmptyRow(row))
            {
                skipped++;
                continue;
            }

            var fechaRaw = Cell(row, ColFecha);
            var merchant = Cell(row, ColMovimiento);
            var importeRaw = Cell(row, ColImporte);

            if (!TryParseDate(fechaRaw, out var purchaseAt))
            {
                diagnostics.Add($"Fila {rowNum}: fecha inválida '{fechaRaw}' — omitida");
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(merchant))
            {
                diagnostics.Add($"Fila {rowNum}: comercio vacío — omitida");
                skipped++;
                continue;
            }

            if (!TryParseAmount(importeRaw, out var amount))
            {
                diagnostics.Add($"Fila {rowNum}: importe inválido '{importeRaw}' — omitida");
                skipped++;
                continue;
            }

            operations.Add(new DebitCardOperation(purchaseAt, merchant.Trim(), amount));
        }

        _logger.LogInformation(
            "BbvaDebitCard parser: {Count} operaciones, {Skipped} omitidas, {Errors} errores | {File}",
            operations.Count, skipped, diagnostics.Count, Path.GetFileName(sourceFile));

        return new ParseResult(operations, skipped, diagnostics);
    }

    // ── Helpers de parsing ────────────────────────────────────────

    private static bool TryParseDate(string? raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return DateTimeOffset.TryParse(
            raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>Formato confirmado en el archivo real: "ARS6232.0" / "ARS39886.67" — siempre positivo.</summary>
    private static bool TryParseAmount(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var normalized = raw.Trim();
        if (normalized.StartsWith("ARS", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];

        return decimal.TryParse(
            normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static void ValidateHeader(string?[] header, List<string> diagnostics)
    {
        var fecha = header.ElementAtOrDefault(ColFecha) ?? string.Empty;
        if (!fecha.Equals("Fecha", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add($"Header inesperado: col0='{fecha}' (esperado 'Fecha') — " +
                            "el parser puede producir resultados incorrectos");
    }

    private static bool IsEmptyRow(string?[] row)
        => row.Length == 0 || row.All(c => string.IsNullOrWhiteSpace(c));

    private static string? Cell(string?[] row, int col)
        => col < row.Length ? row[col] : null;
}
