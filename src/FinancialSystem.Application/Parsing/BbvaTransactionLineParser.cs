using System.Text.RegularExpressions;
using FinancialSystem.Application.Imports.Parsing;
using FinancialSystem.Application.Parsing.Helpers;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Parsing.Bbva;

/// <summary>
/// Parser de líneas de transacción para extractos BBVA Visa Argentina.
///
/// ANATOMÍA DE UNA LÍNEA DE TRANSACCIÓN:
///
///   28-Mar-26 DLO*PEDIDOSYA MCDONALD     003842    32.725,00
///   ─────┬──  ──────────┬────────────  ────┬────  ─────┬─────
///        │              │                  │            │
///      fecha        descripción          cupón        monto
///
///   Variante USD:
///   28-Mar-26 PLAYSTATION USD 4,99       886221    4,99
///   20-Abr-26 NETFLIX.COM 0HnCsFb8GUSD 11,14  584563    11,14
///
/// ESTRATEGIA (sin posiciones fijas):
///   1. Validar que la línea empieza con una fecha DD-Mmm-YY/YYYY
///   2. Capturar el monto final (último token numérico con coma decimal)
///   3. Capturar el cupón (penúltimo token, 4-8 dígitos)
///   4. Todo lo que queda en el medio = descripción
///   5. Detectar moneda por presencia de "USD" en la descripción
///   6. Para USD: extraer monto en USD (que precede al cupón)
/// </summary>
public sealed class BbvaTransactionLineParser
{
    private readonly ILogger<BbvaTransactionLineParser> _logger;

    // ──────────────────────────────────────────────────────────────
    // REGEX PRINCIPALES
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fecha al inicio de línea. Soporta años de 2 o 4 dígitos.
    /// Meses en español abreviados (Mar, Abr, May, Jun, Jul, Ago, Sep, Oct, Nov, Dic, Ene, Feb).
    /// </summary>
    private static readonly Regex DatePattern = new(
        @"^(\d{2}-(?:Ene|Feb|Mar|Abr|May|Jun|Jul|Ago|Sep|Oct|Nov|Dic)-\d{2,4})\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// Número de cupón: 4 a 8 dígitos consecutivos que aparecen
    /// justo antes del monto final, sin letras adyacentes.
    /// </summary>
    private static readonly Regex CouponPattern = new(
        @"\b(\d{4,8})\s+([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Monto final: último valor numérico argentino de la línea.
    /// </summary>
    private static readonly Regex FinalAmountPattern = new(
        @"([\d]{1,3}(?:\.[\d]{3})*,[\d]{1,2}|[\d]+,[\d]{1,2})$",
        RegexOptions.Compiled
    );

    // Mapeo de abreviaturas de mes español → número
    private static readonly Dictionary<string, int> MonthMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ene"] = 1,  ["Feb"] = 2,  ["Mar"] = 3,  ["Abr"] = 4,
        ["May"] = 5,  ["Jun"] = 6,  ["Jul"] = 7,  ["Ago"] = 8,
        ["Sep"] = 9,  ["Oct"] = 10, ["Nov"] = 11, ["Dic"] = 12
    };

    public BbvaTransactionLineParser(ILogger<BbvaTransactionLineParser> logger)
    {
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────
    // API PÚBLICA
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Determina si una línea tiene la estructura de una transacción.
    /// Rápido: sólo valida fecha + presencia de monto. Sin parseo completo.
    /// </summary>
    public bool IsTransactionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 20)
            return false;

        // Condición 1: debe comenzar con fecha española
        if (!DatePattern.IsMatch(line))
            return false;

        // Condición 2: debe terminar con un monto numérico argentino
        if (!FinalAmountPattern.IsMatch(line))
            return false;

        // Condición 3: debe tener cupón antes del monto
        // (filtra líneas como totales o encabezados que terminan en número)
        if (!CouponPattern.IsMatch(line))
            return false;

        return true;
    }

    /// <summary>
    /// Parsea una línea de transacción. Devuelve Fail() con motivo si no puede.
    /// Nunca lanza excepciones por datos malformados.
    /// </summary>
    public ParseResult<Transaction> ParseTransaction(string rawLine)
    {
        if (!IsTransactionLine(rawLine))
            return ParseResult<Transaction>.Fail($"Línea no reconocida como transacción: '{rawLine}'");

        // ── PASO 1: Extraer fecha ──────────────────────────────────
        var dateMatch = DatePattern.Match(rawLine);
        var dateResult = ParseDate(dateMatch.Groups[1].Value);
        if (!dateResult.Success)
            return ParseResult<Transaction>.Fail($"Fecha inválida en: '{rawLine}' → {dateResult.Error}");

        // Resto de la línea sin la fecha
        var remainder = rawLine[dateMatch.Length..].Trim();

        // ── PASO 2: Extraer monto final y cupón ───────────────────
        var couponMatch = CouponPattern.Match(remainder);
        if (!couponMatch.Success)
            return ParseResult<Transaction>.Fail($"No se encontró cupón+monto en: '{remainder}'");

        var couponNumber = couponMatch.Groups[1].Value;
        var rawAmount = couponMatch.Groups[2].Value;

        var amountResult = AmountParser.ParseFromToken(rawAmount);
        if (!amountResult.Success)
            return ParseResult<Transaction>.Fail($"Monto inválido '{rawAmount}': {amountResult.Error}");

        // ── PASO 3: Extraer descripción (lo que queda entre fecha y cupón) ──
        var descriptionEnd = remainder.IndexOf(couponMatch.Value, StringComparison.Ordinal);
        var description = remainder[..descriptionEnd].Trim();

        if (string.IsNullOrWhiteSpace(description))
            return ParseResult<Transaction>.Fail($"Descripción vacía en línea: '{rawLine}'");

        // ── PASO 4: Detectar moneda y monto correcto ──────────────
        var currency = CurrencyDetector.Detect(rawLine);
        var finalAmount = amountResult.Value!;

        // Para transacciones USD, el monto real es el que aparece en USD,
        // no el equivalente en pesos al final de la línea.
        if (currency == "USD")
        {
            var usdAmountResult = CurrencyDetector.TryExtractUsdAmount(rawLine);
            if (usdAmountResult.Success)
            {
                finalAmount = usdAmountResult.Value!;
            }
            else
            {
                // Sin un monto en USD confiable, la transacción se registra en ARS con
                // el monto ya extraído (Paso 2) — moneda e importe deben cambiar juntos,
                // nunca uno sin el otro (ver CurrencyDetector.TryExtractUsdAmount).
                currency = "ARS";
                _logger.LogWarning(
                    "Línea con marca USD pero sin monto en USD extraíble, se registra en ARS. Línea: '{Line}'",
                    rawLine);
            }
        }

        // ── PASO 5: Construir entidad ──────────────────────────────
        var transaction = new Transaction
        {
            Date = dateResult.Value!,
            Description = NormalizeDescription(description),
            Amount = finalAmount,
            Currency = currency,
            CouponNumber = couponNumber,
            RawLine = rawLine
        };

        return ParseResult<Transaction>.Ok(transaction);
    }

    // ──────────────────────────────────────────────────────────────
    // HELPERS PRIVADOS
    // ──────────────────────────────────────────────────────────────

    private ParseResult<DateTime> ParseDate(string raw)
    {
        // Formato: "28-Mar-26" o "28-Mar-2026"
        var parts = raw.Split('-');
        if (parts.Length != 3)
            return ParseResult<DateTime>.Fail($"Formato de fecha inesperado: '{raw}'");

        if (!int.TryParse(parts[0], out var day))
            return ParseResult<DateTime>.Fail($"Día inválido: '{parts[0]}'");

        if (!MonthMap.TryGetValue(parts[1], out var month))
            return ParseResult<DateTime>.Fail($"Mes desconocido: '{parts[1]}'");

        if (!int.TryParse(parts[2], out var year))
            return ParseResult<DateTime>.Fail($"Año inválido: '{parts[2]}'");

        // Año abreviado de 2 dígitos: asumimos 2000-2099
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

    /// <summary>
    /// Limpia la descripción de artefactos del PDF y normaliza espacios.
    /// No trunca ni modifica el contenido semántico.
    /// </summary>
    private static string NormalizeDescription(string raw)
    {
        // Colapsar múltiples espacios en uno
        return Regex.Replace(raw, @"\s{2,}", " ").Trim();
    }
}
