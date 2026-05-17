using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Application.Parsing.Helpers;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Parsing.Mastercard;

/// <summary>
/// Parser de líneas de transacción para extractos Mastercard Argentina.
///
/// ANATOMÍA OBSERVADA EN EXTRACTOS MASTERCARD:
///
///   Formato 1 (más común - fecha dd/MM/yy):
///   15/03/26 PEDIDOSYA*MCDONALD       1234567   15.890,00
///   15/03/26 SPOTIFY                  7654321   USD 5,99     3.456,00
///
///   Formato 2 (variante - fecha dd/MM/yyyy):
///   15/03/2026 PEDIDOSYA*MCDONALD     1234567   15.890,00
///
///   Formato 3 (con cuotas):
///   15/03/26 GARBARINO C1/3           1234567   25.000,00
///
/// DIFERENCIAS CLAVE vs BBVA Visa:
///   - Fecha usa '/' como separador (no '-')
///   - Meses numéricos (no abreviados en español)
///   - USD aparece ANTES del monto en dólares, luego el equivalente ARS al final
///   - Algunas líneas tienen info de cuotas en la descripción (C1/3, C2/3)
///
/// ESTRATEGIA:
///   Misma que BBVA: anclamos desde el final de la línea.
///   Monto final → cupón → descripción. Sin posiciones fijas.
/// </summary>
public sealed class MastercardTransactionLineParser
{
    private readonly ILogger<MastercardTransactionLineParser> _logger;

    // ── REGEX ─────────────────────────────────────────────────────

    /// <summary>
    /// Fecha al inicio: dd/MM/yy o dd/MM/yyyy
    /// Captura el grupo completo para luego parsear.
    /// </summary>
    private static readonly Regex DatePattern = new(
        @"^(\d{2}/\d{2}/\d{2,4})\s+",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Cupón + monto final (ARS): penúltimo y último token.
    /// El cupón es 5-8 dígitos (Mastercard usa 7 típicamente).
    /// El monto ARS siempre es el último token numérico con coma decimal.
    /// </summary>
    private static readonly Regex CouponAndFinalAmountPattern = new(
        @"\b(\d{5,8})\s+([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Monto final argentino al cierre de línea (para validación rápida).
    /// </summary>
    private static readonly Regex FinalAmountPattern = new(
        @"([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Monto USD explícito en la descripción: "USD 5,99" o "U$S 5,99"
    /// Aparece ANTES del cupón y del monto ARS equivalente.
    /// </summary>
    private static readonly Regex UsdInDescriptionPattern = new(
        @"\bU(?:SD|(?:\$S))\s+([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public MastercardTransactionLineParser(ILogger<MastercardTransactionLineParser> logger)
    {
        _logger = logger;
    }

    // ── API PÚBLICA ───────────────────────────────────────────────

    /// <summary>
    /// Validación rápida: fecha dd/MM/yy + monto final + cupón.
    /// No hace parseo completo.
    /// </summary>
    public bool IsTransactionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 18)
            return false;

        if (!DatePattern.IsMatch(line))
            return false;

        if (!FinalAmountPattern.IsMatch(line))
            return false;

        if (!CouponAndFinalAmountPattern.IsMatch(line))
            return false;

        return true;
    }

    /// <summary>
    /// Parsea una línea de transacción Mastercard.
    /// Nunca lanza excepciones por datos malformados.
    /// </summary>
    public ParseResult<Transaction> ParseLine(string rawLine)
    {
        if (!IsTransactionLine(rawLine))
            return ParseResult<Transaction>.Fail($"Línea no reconocida: '{rawLine}'");

        // ── PASO 1: Fecha ─────────────────────────────────────────
        var dateMatch = DatePattern.Match(rawLine);
        var dateResult = ParseDate(dateMatch.Groups[1].Value);
        if (!dateResult.Success)
            return ParseResult<Transaction>.Fail($"Fecha inválida: '{dateMatch.Groups[1].Value}' → {dateResult.Error}");

        var remainder = rawLine[dateMatch.Length..].Trim();

        // ── PASO 2: Cupón + monto ARS final ──────────────────────
        var couponMatch = CouponAndFinalAmountPattern.Match(remainder);
        if (!couponMatch.Success)
            return ParseResult<Transaction>.Fail($"No se encontró cupón+monto en: '{remainder}'");

        var couponNumber = couponMatch.Groups[1].Value;
        var rawArsAmount = couponMatch.Groups[2].Value;

        // ── PASO 3: Descripción (entre fecha y cupón) ─────────────
        var descEnd = remainder.IndexOf(couponMatch.Value, StringComparison.Ordinal);
        var rawDescription = remainder[..descEnd].Trim();

        if (string.IsNullOrWhiteSpace(rawDescription))
            return ParseResult<Transaction>.Fail($"Descripción vacía en: '{rawLine}'");

        // ── PASO 4: Moneda y monto correcto ──────────────────────
        // Mastercard: si hay "USD X,XX" en la descripción, la transacción
        // es en dólares. El monto final (ARS) es el equivalente convertido
        // que NO queremos guardar como monto — guardamos el USD original.
        var usdMatch = UsdInDescriptionPattern.Match(rawDescription);
        string currency;
        decimal amount;

        if (usdMatch.Success)
        {
            // Transacción USD: extraer monto en dólares de la descripción
            var usdAmountResult = AmountParser.ParseFromToken(usdMatch.Groups[1].Value);
            if (!usdAmountResult.Success)
            {
                _logger.LogWarning(
                    "Línea con USD pero monto dólar inextractable, usando ARS. Línea: '{Line}'",
                    rawLine);
                var arsResult = AmountParser.ParseFromToken(rawArsAmount);
                if (!arsResult.Success)
                    return ParseResult<Transaction>.Fail($"Monto ARS inválido: '{rawArsAmount}'");
                currency = "ARS";
                amount = arsResult.Value!;
            }
            else
            {
                currency = "USD";
                amount = usdAmountResult.Value!;
            }

            // Limpiar la marca USD de la descripción para que quede limpia
            rawDescription = UsdInDescriptionPattern.Replace(rawDescription, string.Empty).Trim();
        }
        else
        {
            currency = "ARS";
            var arsResult = AmountParser.ParseFromToken(rawArsAmount);
            if (!arsResult.Success)
                return ParseResult<Transaction>.Fail($"Monto ARS inválido '{rawArsAmount}': {arsResult.Error}");
            amount = arsResult.Value!;
        }

        // ── PASO 5: Construir entidad ─────────────────────────────
        var transaction = new Transaction
        {
            Date = dateResult.Value!,
            Description = NormalizeDescription(rawDescription),
            Amount = amount,
            Currency = currency,
            CouponNumber = couponNumber,
            RawLine = rawLine
        };

        return ParseResult<Transaction>.Ok(transaction);
    }

    // ── HELPERS PRIVADOS ──────────────────────────────────────────

    private static ParseResult<DateTime> ParseDate(string raw)
    {
        // Formato: dd/MM/yy o dd/MM/yyyy
        var parts = raw.Split('/');
        if (parts.Length != 3)
            return ParseResult<DateTime>.Fail($"Formato inesperado: '{raw}'");

        if (!int.TryParse(parts[0], out var day))
            return ParseResult<DateTime>.Fail($"Día inválido: '{parts[0]}'");

        if (!int.TryParse(parts[1], out var month) || month < 1 || month > 12)
            return ParseResult<DateTime>.Fail($"Mes inválido: '{parts[1]}'");

        if (!int.TryParse(parts[2], out var year))
            return ParseResult<DateTime>.Fail($"Año inválido: '{parts[2]}'");

        if (year < 100)
            year += 2000;

        try
        {
            return ParseResult<DateTime>.Ok(new DateTime(year, month, day));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ParseResult<DateTime>.Fail($"Fecha imposible: {day}/{month}/{year}");
        }
    }

    private static string NormalizeDescription(string raw)
    {
        // Normalizar espacios múltiples (artefacto común en texto extraído de PDF)
        var collapsed = Regex.Replace(raw, @"\s{2,}", " ").Trim();

        // Normalizar info de cuotas: "C 1/ 3" → "C1/3" (PdfPig a veces separa)
        collapsed = Regex.Replace(collapsed, @"\bC\s*(\d+)\s*/\s*(\d+)\b", "C$1/$2");

        return collapsed;
    }
}
