using System;
using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports.Parsing;

namespace FinancialSystem.Application.Parsing.Helpers
{
    public static class CurrencyDetector
    {
        private static readonly Regex UsdWordRegex = new(@"\bUSD\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UsdAmountRegex = new(@"\bUSD\s*([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyAmountRegex = new(@"([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})", RegexOptions.Compiled);

        // Detecta la moneda básica por presencia de 'USD' en la línea. Por defecto ARS.
        public static string Detect(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return "ARS";

            return UsdWordRegex.IsMatch(rawLine) ? "USD" : "ARS";
        }

        // Intenta extraer un monto en USD cercano a la marca 'USD'.
        // Devuelve ParseResult<decimal> usando AmountParser.
        public static ParseResult<decimal> TryExtractUsdAmount(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return ParseResult<decimal>.Fail("Cadena vacía");

            // 1) Buscamos patrón explícito: 'USD 4,99' (caso más común)
            var m = UsdAmountRegex.Match(rawLine);
            if (m.Success)
            {
                var token = m.Groups[1].Value;
                return AmountParser.ParseFromToken(token);
            }

            // 2) Si 'USD' aparece pero sin monto inmediato, buscamos el primer monto numérico tras 'USD'
            var idx = rawLine.IndexOf("USD", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var after = rawLine.Substring(idx + 3);
                var amtMatch = AnyAmountRegex.Match(after);
                if (amtMatch.Success)
                    return AmountParser.ParseFromToken(amtMatch.Value);
            }

            // 3) No se encontró monto USD
            return ParseResult<decimal>.Fail("No se encontró monto USD");
        }
    }
}
