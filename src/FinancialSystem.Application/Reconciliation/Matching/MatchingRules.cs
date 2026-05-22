using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Application.Reconciliation.Matching;

// ════════════════════════════════════════════════════════════════════
// REGLAS DE MATCHING
//
// Cada regla es independiente, stateless, testeable en aislamiento.
// Ninguna regla conoce a las otras ni al motor.
//
// DISEÑO DEL SCORE:
//   - 1.0 = match perfecto en esta dimensión
//   - 0.0 = sin similitud
//   - Los valores intermedios representan similitud parcial
//
// DISEÑO DE TOLERANCIAS:
//   Las tolerancias vienen de ReconciliationOptions inyectado,
//   nunca hardcodeadas en las reglas.
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Regla de monto con tolerancia absoluta y relativa.
///
/// HEURÍSTICA:
///   - Diferencia ≤ tolerancia absoluta (ej: 50 ARS) → score alto
///   - Diferencia ≤ tolerancia relativa (ej: 2%) → score alto
///   - Entre 1x y 3x la tolerancia → score degradado linealmente
///   - Diferencia > 3x tolerancia → score 0
///
/// CASO REAL:
///   banco = 4660.45, manual = 4700 → delta = 39.55
///   Con tolerancia absoluta = 50 → score = 1.0
/// </summary>
public sealed class AmountMatchingRule : IMatchingRule
{
    private readonly ReconciliationOptions _opts;

    public AmountMatchingRule(IOptions<ReconciliationOptions> opts) => _opts = opts.Value;

    public string RuleName => "Amount";
    public double Weight => _opts.AmountRuleWeight;
    public bool IsHardConstraint => false;

    public (double Score, string? Detail) Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var delta = Math.Abs(reference.Amount - candidate.Amount);
        var refAmount = Math.Max(reference.Amount, 0.01m);  // evitar div/0

        // Tolerancia efectiva: la mayor entre absoluta y relativa
        var absoluteTol = _opts.AmountAbsoluteTolerance;
        var relativeTol = refAmount * (decimal)_opts.AmountRelativeTolerance;
        var effectiveTol = Math.Max(absoluteTol, relativeTol);

        if (delta == 0m)
            return (1.0, "Monto exacto");

        if (delta <= effectiveTol)
        {
            // Score degradado levemente incluso dentro de tolerancia
            // para que un match exacto siempre supere a uno aproximado
            var score = 1.0 - (double)(delta / effectiveTol) * 0.15;
            return (score, $"Delta {delta:N2} ARS (tol={effectiveTol:N2})");
        }

        // Degradación lineal entre 1x y 3x la tolerancia
        var hardLimit = effectiveTol * 3;
        if (delta <= hardLimit)
        {
            var score = 0.5 * (1.0 - (double)((delta - effectiveTol) / (hardLimit - effectiveTol)));
            return (Math.Max(0.0, score), $"Delta {delta:N2} ARS (fuera de tol, degradado)");
        }

        return (0.0, $"Delta {delta:N2} ARS (excede límite {hardLimit:N2})");
    }
}

/// <summary>
/// Regla de fecha con ventana configurable.
///
/// HEURÍSTICA:
///   - Misma fecha → 1.0
///   - 1 día → 0.85 (débitos bancarios suelen acreditar al día siguiente)
///   - 2 días → 0.65
///   - 3 días (límite default) → 0.40
///   - > ventana → 0.0
///
/// Los scores específicos aseguran que una diferencia de 1 día
/// siga siendo un match válido pero siempre peor que misma fecha.
/// </summary>
public sealed class DateMatchingRule : IMatchingRule
{
    private readonly ReconciliationOptions _opts;

    public DateMatchingRule(IOptions<ReconciliationOptions> opts) => _opts = opts.Value;

    public string RuleName => "Date";
    public double Weight => _opts.DateRuleWeight;
    public bool IsHardConstraint => false;

    public (double Score, string? Detail) Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var deltaDays = Math.Abs((reference.Date.Date - candidate.Date.Date).TotalDays);

        if (deltaDays == 0)
            return (1.0, "Fecha exacta");

        if (deltaDays > _opts.DateWindowDays)
            return (0.0, $"{deltaDays} días (fuera de ventana de {_opts.DateWindowDays}d)");

        // Decaimiento lineal dentro de la ventana
        var score = 1.0 - (deltaDays / (_opts.DateWindowDays + 1.0));
        return (score, $"{deltaDays} día(s) de diferencia");
    }
}

/// <summary>
/// Regla de descripción usando matching fuzzy por tokens.
///
/// ESTRATEGIA (sin ML):
///   1. Normalización: mayúsculas, sin tildes, sin caracteres especiales
///   2. Tokenización: split por espacios y caracteres no alfanuméricos
///   3. Filtrado de stop words (SA, SRL, DE, EL, LA...)
///   4. Jaccard similarity sobre el conjunto de tokens
///   5. Bonus por substring: si uno contiene al otro (ej: "FARMACITY" ⊂ "FARMACITY SA")
///   6. Boost si hay token exacto en común (ej: "NETFLIX" = "NETFLIX")
///
/// CASO REAL:
///   "Farmacia" vs "FARMACITY SA"
///   Tokens: {farmacia} vs {farmacity, sa}
///   Bonus substring: "farmaci" ⊂ "farmacity" → boost
///   Score final: ~0.65 (descripción diferente pero relacionada)
/// </summary>
public sealed class DescriptionMatchingRule : IMatchingRule
{
    private readonly ReconciliationOptions _opts;

    // Stop words argentinas comunes en descripciones bancarias
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sa", "srl", "sas", "de", "el", "la", "los", "las",
        "del", "y", "e", "en", "a", "con", "por", "para",
        "ar", "arg", "argentina",
    };

    public DescriptionMatchingRule(IOptions<ReconciliationOptions> opts) => _opts = opts.Value;

    public string RuleName => "Description";
    public double Weight => _opts.DescriptionRuleWeight;
    public bool IsHardConstraint => false;

    public (double Score, string? Detail) Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var refNorm = Normalize(reference.Description);
        var candNorm = Normalize(candidate.Description);

        if (refNorm == candNorm)
            return (1.0, "Descripción exacta (normalizada)");

        var refTokens = Tokenize(refNorm);
        var candTokens = Tokenize(candNorm);

        if (refTokens.Count == 0 || candTokens.Count == 0)
            return (0.0, "Sin tokens útiles");

        // Jaccard base
        var intersection = refTokens.Intersect(candTokens).Count();
        var union = refTokens.Union(candTokens).Count();
        var jaccard = (double)intersection / union;

        // Bonus por prefijo común o substring
        var substringBonus = ComputeSubstringBonus(refNorm, candNorm);

        // Bonus si algún token significativo coincide exactamente
        var exactTokenBonus = refTokens.Any(t => t.Length > 4 && candTokens.Contains(t)) ? 0.15 : 0.0;

        var totalScore = Math.Min(1.0, jaccard + substringBonus + exactTokenBonus);

        if (totalScore < _opts.DescriptionMinimumSimilarity)
            return (0.0, $"Similitud {totalScore:P0} por debajo del mínimo");

        return (totalScore, $"Jaccard={jaccard:P0} sub={substringBonus:P0} tok={exactTokenBonus:P0}");
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var result = input.ToUpperInvariant();

        // Normalizar tildes
        result = result
            .Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I')
            .Replace('Ó', 'O').Replace('Ú', 'U').Replace('Ü', 'U')
            .Replace('Ñ', 'N');

        // Conservar sólo alfanuméricos y espacios
        var chars = result.Where(c => char.IsLetterOrDigit(c) || c == ' ');
        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static HashSet<string> Tokenize(string normalized)
    {
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static double ComputeSubstringBonus(string a, string b)
    {
        // Si uno empieza con el otro (ej: "FARMACI" vs "FARMACITY")
        if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
            return 0.25;

        // Si uno contiene al otro
        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
            b.Contains(a, StringComparison.OrdinalIgnoreCase))
            return 0.20;

        // Longest common subsequence aproximado (primeros N chars)
        var prefixA = a.Length >= 6 ? a[..6] : a;
        var prefixB = b.Length >= 6 ? b[..6] : b;
        if (prefixA == prefixB)
            return 0.15;

        return 0.0;
    }
}

/// <summary>
/// Regla de método de pago.
///
/// HEURÍSTICA:
///   - Si el movimiento manual no declara método → neutro (no penaliza)
///   - Si declara "crédito" y el reference es tarjeta → boost
///   - Si declara "débito" y el reference es banco → boost
///   - Si declara "efectivo" → nunca matchea con banco/tarjeta (constraint)
///
/// IMPORTANTE: es la regla de menor peso porque la información
/// de método de pago es incompleta (muchas veces no se carga).
/// </summary>
public sealed class PaymentMethodMatchingRule : IMatchingRule
{
    private readonly ReconciliationOptions _opts;

    public PaymentMethodMatchingRule(IOptions<ReconciliationOptions> opts) => _opts = opts.Value;

    public string RuleName => "PaymentMethod";
    public double Weight => _opts.PaymentMethodRuleWeight;
    public bool IsHardConstraint => false;  // efectivo es constraint pero lo manejamos con score 0

    public (double Score, string? Detail) Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        // Si el candidato (manual) no declara método de pago → neutral
        if (candidate.PaymentMethod is null)
            return (0.5, "Método no declarado (neutral)");

        return (candidate.PaymentMethod, reference.Source) switch
        {
            // Efectivo nunca debería aparecer en banco ni tarjeta
            (PaymentMethod.Cash, MovementSource.BankDebit) =>
                (0.0, "Efectivo no puede matchear con débito bancario"),
            (PaymentMethod.Cash, MovementSource.CreditCard) =>
                (0.0, "Efectivo no puede matchear con tarjeta"),

            // Débito con banco → match esperado
            (PaymentMethod.Debit, MovementSource.BankDebit) =>
                (1.0, "Débito con movimiento bancario"),

            // Crédito con tarjeta → match esperado
            (PaymentMethod.Credit, MovementSource.CreditCard) =>
                (1.0, "Crédito con movimiento de tarjeta"),

            // Combinaciones cruzadas → penalización leve (puede ser error de carga)
            (PaymentMethod.Debit, MovementSource.CreditCard) =>
                (0.2, "Débito declarado pero fuente es tarjeta"),
            (PaymentMethod.Credit, MovementSource.BankDebit) =>
                (0.2, "Crédito declarado pero fuente es banco"),

            // Transferencia → neutral
            (PaymentMethod.Transfer, _) =>
                (0.5, "Transferencia (neutral)"),

            _ => (0.5, "Combinación no reconocida (neutral)"),
        };
    }
}

/// <summary>
/// Hard constraint: monedas distintas = score 0, siempre.
/// No es una "regla" en el sentido de scoring — si falla, el par
/// se descarta antes de calcular el score compuesto.
/// </summary>
public sealed class CurrencyConstraint : IMatchingRule
{
    public string RuleName => "Currency";
    public double Weight => 0.0;     // No aporta al score; es un filtro binario
    public bool IsHardConstraint => true;

    public (double Score, string? Detail) Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var match = string.Equals(reference.Currency, candidate.Currency, StringComparison.OrdinalIgnoreCase);
        return match
            ? (1.0, null)
            : (0.0, $"Monedas incompatibles: {reference.Currency} vs {candidate.Currency}");
    }
}
