namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Cómo fue procesado este gasto. Reemplaza ConfirmationSource + ReconciliationGroupingMode.
    /// Permite auditar el origen del procesamiento sin necesitar dos campos separados.
    /// El MCP no lo usa para cálculos, solo para trazabilidad.
    /// </summary>
    public enum ProcessingSource
    {
        /// <summary>
        /// El usuario armó el match manualmente sin sugerencia del motor.
        /// Puede ser 1↔1, N↔M manual.
        /// </summary>
        ManualMatch = 1,

        /// <summary>
        /// El usuario confirmó una sugerencia de confianza media generada por el motor.
        /// </summary>
        ConfirmedFromSuggestion = 2,

        /// <summary>
        /// El usuario confirmó una sugerencia de alta confianza (AutoConfirmable).
        /// </summary>
        ConfirmedFromAutoSuggestion = 3,

        /// <summary>
        /// El usuario marcó el movimiento como revisado manualmente, sin contraparte Excel.
        /// Siempre corresponde a Status = Reviewed.
        /// </summary>
        ManualReview = 4,
    }

}
