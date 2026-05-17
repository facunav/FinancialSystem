using FinancialSystem.Application.Imports.Parsing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FinancialSystem.Application.Parsing.Helpers;

/// <summary>
/// Normaliza montos argentinos con sus particularidades:
///   - Separador de miles: punto  → 32.725
///   - Separador decimal: coma   → 32.725,00
///   - Montos USD: pueden ser    → 4,99  o  11,14  (sin separador de miles)
///
/// IMPORTANTE: No confundir "4,99" (ARS: cuatro con noventa y nueve)
///             con "4.99" que es el mismo valor en formato anglosajón.
/// </summary>
public static class AmountParser
{
    // Captura el último token numérico de la línea (el monto final).
    // Soporta: 32.725,00 | 1.000,00 | 4,99 | 11,14 | 30.899,97
    private static readonly Regex AmountPattern = new(
        @"([\d]{1,3}(?:\.[\d]{3})*(?:,[\d]{1,2})|[\d]+,[\d]{1,2})$",
        RegexOptions.Compiled | RegexOptions.RightToLeft
    );

    public static ParseResult<decimal> TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ParseResult<decimal>.Fail("Cadena vacía");

        var match = AmountPattern.Match(raw);
        if (!match.Success)
            return ParseResult<decimal>.Fail($"No se encontró patrón de monto en: '{raw}'");

        return Normalize(match.Value);
    }

    /// <summary>
    /// Extrae el monto de una posición conocida dentro de la línea.
    /// Más preciso cuando ya sabemos dónde termina la línea.
    /// </summary>
    public static ParseResult<decimal> ParseFromToken(string token)
    {
        return Normalize(token.Trim());
    }

    private static ParseResult<decimal> Normalize(string raw)
    {
        // Estrategia: Argentina usa punto como miles y coma como decimal.
        // Eliminamos los puntos de miles y reemplazamos la coma decimal por punto.
        //
        // "32.725,00" → "32725.00" → 32725.00
        // "4,99"      → "4.99"     → 4.99
        // "1.000,00"  → "1000.00"  → 1000.00

        var normalized = raw
            .Replace(".", string.Empty)   // quitar separadores de miles
            .Replace(",", ".");           // normalizar decimal

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            return ParseResult<decimal>.Ok(result);

        return ParseResult<decimal>.Fail($"No se pudo parsear el monto normalizado: '{normalized}' (original: '{raw}')");
    }
}
