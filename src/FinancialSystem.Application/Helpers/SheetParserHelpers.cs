using System.Security.Cryptography;
using System.Text;

namespace FinancialSystem.Application.Helpers
{
    public static class SheetParserHelpers
    {
        /// <summary>
        /// Normaliza la forma de pago tolerando el typo "Creidito" del Excel real
        /// y otras variantes comunes.
        /// </summary>
        public static string NormalizePaymentMethod(string raw)
        {
            return raw.Trim().ToUpperInvariant() switch
            {
                "DEBITO" or "DÉBITO" => "Debito",
                "CREDITO" or "CRÉDITO" or "CREIDITO" // typo real en el Excel
                           or "CREDIT" => "Credito",
                "EFECTIVO" or "EFE" or "CASH" => "Efectivo",
                "TRANSFERENCIA" or "TRANS" or "TRF" => "Transferencia",
                var other when string.IsNullOrWhiteSpace(other) => string.Empty,
                var other => other, // preservar valores desconocidos sin romper
            };
        }

        /// <summary>
        /// Calcula el ExternalId determinístico para idempotencia.
        /// SHA256("{sourceFile}|{sheetName}|{rowNumber}") → hex lowercase de 64 chars.
        /// </summary>
        public static string BuildExternalId(string sourceFile, string sheetName, int rowNumber)
        {
            var raw = $"{Path.GetFileName(sourceFile)}|{sheetName}|{rowNumber}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
