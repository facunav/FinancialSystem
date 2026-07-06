namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Cómo fue procesado este movimiento. Para trazabilidad y auditoría.
/// El MCP no lo usa para cálculos.
/// </summary>
public enum ProcessingSource
{
    /// <summary>
    /// El usuario armó la coincidencia manualmente sin sugerencia del motor.
    /// </summary>
    ManualMatch = 1,

    /// <summary>
    /// El usuario confirmó una sugerencia de confianza media generada por el motor.
    /// </summary>
    ConfirmedFromSuggestion = 2,

    /// <summary>
    /// El usuario confirmó una sugerencia de alta confianza (auto-confirmable).
    /// </summary>
    ConfirmedFromHighConfidenceSuggestion = 3,

    /// <summary>
    /// El usuario clasificó el movimiento manualmente, sin coincidencia externa.
    /// Siempre corresponde a ClassificationStatus = Reviewed.
    /// </summary>
    ManualReview = 4,
}