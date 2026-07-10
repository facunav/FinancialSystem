namespace FinancialSystem.Application.Review;

/// <summary>
/// Parámetros que controlan las heurísticas del motor de revisión.
///
/// PR-L4: hasta acá también incluía umbrales de confianza, tolerancias y pesos de
/// reglas para el matching contra movimientos "Candidate" (legacy Excel) — ese
/// mecanismo se retiró completo (ver ReviewResult.cs). Quedan únicamente los
/// parámetros de ISuspicionDetector, que nunca dependieron de Candidate.
/// </summary>
public sealed class ReviewEngineOptions
{
    public const string SectionName = "ReviewEngine";

    // ── Detección de duplicados ──────────────────────────────────
    /// <summary>Ventana de días para buscar posibles duplicados dentro de la misma fuente.</summary>
    public int DuplicateDetectionWindowDays { get; set; } = 1;

    /// <summary>Diferencia máxima de monto para sospechar duplicado.</summary>
    public decimal DuplicateAmountTolerance { get; set; } = 1m;
}
