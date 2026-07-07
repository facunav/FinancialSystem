namespace FinancialSystem.Application.Review;

/// <summary>
/// Parámetros que controlan las heurísticas del motor de sugerencias de revisión.
/// Reemplaza conceptualmente a la vieja ReconciliationOptions (eliminada sin bindear en PR11).
/// Valores default heredados de esa clase; su vigencia para este motor es la Decisión #5
/// del ADR (Review & Classification Engine v2), pendiente de confirmación.
/// </summary>
public sealed class ReviewEngineOptions
{
    public const string SectionName = "ReviewEngine";

    // ── Umbrales de confianza ────────────────────────────────────
    /// <summary>Score mínimo para considerar un match de alta confianza.</summary>
    public double HighConfidenceThreshold { get; set; } = 0.85;

    /// <summary>Score mínimo para considerar un match de confianza media.</summary>
    public double MediumConfidenceThreshold { get; set; } = 0.60;

    /// <summary>Score mínimo para reportar como "near miss" (no matchea pero estuvo cerca).</summary>
    public double NearMissThreshold { get; set; } = 0.35;

    // ── Tolerancias de monto ─────────────────────────────────────
    /// <summary>Diferencia máxima absoluta en ARS para considerar montos iguales.</summary>
    public decimal AmountAbsoluteTolerance { get; set; } = 50m;

    /// <summary>Diferencia máxima relativa. El motor usa max(absoluta, relativa).</summary>
    public double AmountRelativeTolerance { get; set; } = 0.02;

    // ── Tolerancias de fecha ─────────────────────────────────────
    /// <summary>Ventana de días para considerar fechas "cercanas".</summary>
    public int DateWindowDays { get; set; } = 3;

    // ── Pesos de reglas ──────────────────────────────────────────
    // Los pesos se normalizan automáticamente por IMatchScorer,
    // por lo que sólo importan en proporción relativa.
    public double AmountRuleWeight { get; set; } = 0.45;
    public double DateRuleWeight { get; set; } = 0.25;
    public double DescriptionRuleWeight { get; set; } = 0.20;
    public double PaymentMethodRuleWeight { get; set; } = 0.10;

    // ── Detección de duplicados ──────────────────────────────────
    /// <summary>Ventana de días para buscar posibles duplicados dentro de la misma fuente.</summary>
    public int DuplicateDetectionWindowDays { get; set; } = 1;

    /// <summary>Diferencia máxima de monto para sospechar duplicado.</summary>
    public decimal DuplicateAmountTolerance { get; set; } = 1m;

    // ── Fuzzy description matching ───────────────────────────────
    /// <summary>Score mínimo de similitud de descripción para aportar puntaje.</summary>
    public double DescriptionMinimumSimilarity { get; set; } = 0.25;
}
