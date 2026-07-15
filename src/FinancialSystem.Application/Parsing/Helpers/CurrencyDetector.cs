using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports.Parsing;

namespace FinancialSystem.Application.Parsing.Helpers
{
    public static class CurrencyDetector
    {
        private static readonly Regex UsdWordRegex = new(@"\bUSD\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UsdAmountRegex = new(@"\bUSD\s*([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Detecta la moneda básica por presencia de 'USD' en la línea. Por defecto ARS.
        public static string Detect(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return "ARS";

            return UsdWordRegex.IsMatch(rawLine) ? "USD" : "ARS";
        }

        // Intenta extraer un monto en USD pegado a la marca 'USD' (ej. "USD 4,99").
        // Devuelve ParseResult<decimal> usando AmountParser.
        //
        // Deliberadamente NO hay un segundo intento que busque "cualquier monto tras
        // USD": ese monto sería, en la práctica, el total en pesos al final de la
        // línea (el único número que suele quedar disponible) — devolverlo como si
        // fuera el monto en dólares produce Currency=USD con Amount en pesos. Sin un
        // monto explícito junto a 'USD', es más seguro fallar y que el llamador decida
        // el fallback (ver BbvaTransactionLineParser.ParseTransaction) que adivinar.
        public static ParseResult<decimal> TryExtractUsdAmount(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return ParseResult<decimal>.Fail("Cadena vacía");

            var m = UsdAmountRegex.Match(rawLine);
            if (m.Success)
            {
                var token = m.Groups[1].Value;
                return AmountParser.ParseFromToken(token);
            }

            return ParseResult<decimal>.Fail("No se encontró monto USD junto a la marca 'USD'");
        }
    }
}
