using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.BankStatements;

/// <summary>
/// Parsea las filas ya leídas del XLS BBVA Caja de Ahorros.
///
/// ESTRUCTURA CONFIRMADA CON EL ARCHIVO REAL:
///   Fila 0 (base-0): "Detalle de Movimientos de Cuenta: CA$ 214-45099/4"
///   Fila 1:          "Fecha | Concepto | (vacío) | Importe | Saldo"
///   Fila 2+:         Datos de movimientos
///
/// COLUMNAS (base-0):
///   0 = Fecha    → "dd/MM/yyyy" (siempre string en este archivo)
///   1 = Concepto → descripción principal del movimiento
///   2 = Detalle  → canal ("100 - BANCA ONLINE", "733 - ", etc.)
///   3 = Importe  → monto en formato ARS ("-10.000,00" o "14.000,00")
///   4 = Saldo    → saldo post-movimiento en formato ARS
///
/// RESPONSABILIDAD: solo parsear → BankStatement[].
/// No conoce NPOI, EF Core, ni ninguna infraestructura.
/// </summary>
public sealed class BbvaBankStatementParser
{
    private const string BankName  = "BBVA";
    private const int TitleRowIdx  = 0;
    private const int HeaderRowIdx = 1;
    private const int DataStartIdx = 2;

    private const int ColFecha    = 0;
    private const int ColConcepto = 1;
    private const int ColDetalle  = 2;
    private const int ColImporte  = 3;
    private const int ColSaldo    = 4;

    // Detecta "NNN - " sin descripción útil ("733 - ", "569 - ")
    private static readonly Regex EmptyDetailPattern =
        new(@"^\d+\s*-\s*$", RegexOptions.Compiled);

    // Extrae número de cuenta del título "CA$ 214-45099/4" → "214-45099/4"
    private static readonly Regex AccountPattern =
        new(@"CA\$\s*([\w\-/]+)", RegexOptions.Compiled);

    private readonly ILogger<BbvaBankStatementParser> _logger;

    public BbvaBankStatementParser(ILogger<BbvaBankStatementParser> logger)
        => _logger = logger;

    public sealed record ParseResult(
        IReadOnlyList<BankStatement> Statements,
        int SkippedRows,
        IReadOnlyList<string> Diagnostics);

    public ParseResult Parse(
        IReadOnlyList<string?[]> rows,
        string sourceFile,
        string sheetName)
    {
        var statements  = new List<BankStatement>();
        var diagnostics = new List<string>();
        var skipped     = 0;

        if (rows.Count < DataStartIdx + 1)
        {
            diagnostics.Add("Archivo sin filas de datos (menos de 3 filas totales)");
            return new ParseResult(statements, 0, diagnostics);
        }

        var accountNumber = ExtractAccountNumber(
            rows[TitleRowIdx].ElementAtOrDefault(ColFecha) ?? string.Empty);

        ValidateHeader(rows[HeaderRowIdx], diagnostics);

        for (var i = DataStartIdx; i < rows.Count; i++)
        {
            var row    = rows[i];
            var rowNum = i + 1;  // 1-based para trazabilidad

            if (IsEmptyRow(row))
            {
                skipped++;
                continue;
            }

            var fechaRaw   = Cell(row, ColFecha);
            var concepto   = Cell(row, ColConcepto);
            var detalle    = Cell(row, ColDetalle);
            var importeRaw = Cell(row, ColImporte);
            var saldoRaw   = Cell(row, ColSaldo);

            if (!TryParseDate(fechaRaw, out var date))
            {
                diagnostics.Add($"Fila {rowNum}: fecha inválida '{fechaRaw}' — omitida");
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(concepto))
            {
                diagnostics.Add($"Fila {rowNum}: concepto vacío — omitida");
                skipped++;
                continue;
            }

            if (!TryParseArgentineDecimal(importeRaw, out var amount))
            {
                diagnostics.Add($"Fila {rowNum}: importe inválido '{importeRaw}' — omitida");
                skipped++;
                continue;
            }

            TryParseArgentineDecimal(saldoRaw, out var balance);

            statements.Add(new BankStatement
            {
                Date          = DateTime.SpecifyKind(date, DateTimeKind.Utc),
                Concept       = concepto.Trim(),
                Detail        = CleanDetail(detalle),
                Amount        = amount,
                Currency      = "ARS",
                Balance       = balance == 0m && string.IsNullOrWhiteSpace(saldoRaw)
                                    ? null
                                    : balance,
                BankName      = BankName,
                AccountNumber = accountNumber,
                ExternalId    = BuildExternalId(sourceFile, sheetName, rowNum),
                SourceFile    = sourceFile,
                SheetName     = sheetName,
                RowNumber     = rowNum,
                ImportedAtUtc = DateTime.UtcNow,
            });
        }

        _logger.LogInformation(
            "BBVA parser: {Count} movimientos, {Skipped} omitidas, {Errors} errores | {File}",
            statements.Count, skipped, diagnostics.Count, Path.GetFileName(sourceFile));

        return new ParseResult(statements, skipped, diagnostics);
    }

    // ── Helpers de parsing ────────────────────────────────────────

    private static bool TryParseDate(string? raw, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        return DateTime.TryParseExact(
            raw.Trim(),
            ["dd/MM/yyyy", "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    /// <summary>
    /// Formato ARS confirmado: punto=miles, coma=decimal.
    /// "-10.000,00" → -10000.00
    /// "14.000,00"  → 14000.00
    /// "7,94"       → 7.94
    /// </summary>
    private static bool TryParseArgentineDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var normalized = raw.Trim()
            .Replace(".", string.Empty)
            .Replace(",", ".");

        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string? CleanDetail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        // "733 - " sin descripción → ruido, descartamos
        return EmptyDetailPattern.IsMatch(trimmed) ? null : trimmed;
    }

    private static string? ExtractAccountNumber(string title)
    {
        var match = AccountPattern.Match(title);
        return match.Success ? match.Groups[1].Value.Trim() : null;
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

    // ── ExternalId ────────────────────────────────────────────────

    public static string BuildExternalId(string sourceFile, string sheetName, int rowNumber)
    {
        var raw   = $"{Path.GetFileName(sourceFile)}|{sheetName}|row|{rowNumber}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
