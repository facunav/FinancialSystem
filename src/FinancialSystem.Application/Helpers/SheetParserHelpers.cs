using System.Globalization;
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

        /// <summary>
        /// Calcula el ExternalId determinístico para idempotencia de Transaction (tarjeta).
        /// Preferencia: CouponNumber (identificador de operación real que da el banco) cuando existe.
        /// Fallback: SHA256("{date}|{amount}|{description}") cuando no está disponible.
        /// En ambos casos el resultado es SHA256 → hex lowercase de 64 chars, igual formato que
        /// BuildExternalId, para mantener compatibilidad con el resto de las columnas ExternalId.
        ///
        /// Única fuente de verdad para la identidad de una Transaction: tanto la idempotencia
        /// entre corridas (columna ExternalId, índice único) como la deduplicación dentro de un
        /// mismo archivo (ver ImportFileProcessingSink) usan este mismo cálculo — dos filas con
        /// el mismo ExternalId son, por definición, la misma transacción.
        /// </summary>
        public static string BuildTransactionExternalId(
            DateTime date,
            decimal amount,
            string description,
            string? couponNumber)
        {
            var normalizedDescription = description.Trim().ToUpperInvariant();

            var raw = !string.IsNullOrWhiteSpace(couponNumber)
                            ? $"coupon|{couponNumber.Trim()}"
                            : $"{date:O}|{amount.ToString(CultureInfo.InvariantCulture)}|{normalizedDescription}";

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
