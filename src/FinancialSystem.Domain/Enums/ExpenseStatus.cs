namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Estado de un gasto procesado.
    /// Solo existen dos estados finales: Confirmed y Reviewed.
    /// Las sugerencias del motor viven en ReconciliationSuggestion, no aquí.
    /// Todo ProcessedExpense representa verdad financiera verificada.
    /// </summary>
    public enum ExpenseStatus
    {
        /// <summary>
        /// El usuario confirmó el match con una contraparte manual (Excel).
        /// Puede ser 1↔1, N↔1, 1↔N, N↔M.
        /// </summary>
        Confirmed = 1,

        /// <summary>
        /// El usuario revisó el movimiento manualmente sin contraparte.
        /// Requiere ReviewReason y CategoryId obligatorios.
        /// Ejemplos: transferencias, comisiones, regalos.
        /// </summary>
        Reviewed = 2,
    }
}
