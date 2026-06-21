namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Estados del ciclo de vida de una reconciliación.
    /// </summary>
    public enum ReconciledExpenseStatus
    {
        /// <summary>
        /// Generado por el motor para revisión. Confianza media o sin clasificar.
        /// No representa verdad financiera todavía.
        /// </summary>
        Suggested = 0,

        /// <summary>
        /// Alta confianza: pre-seleccionado para confirmación en batch desde UI.
        /// Todavía no es verdad financiera hasta que el usuario confirme.
        /// </summary>
        AutoConfirmed = 1,

        /// <summary>
        /// Usuario confirmó explícitamente. Representa verdad financiera oficial.
        /// ConfirmedAt y ConfirmedBy son obligatorios en este estado.
        /// </summary>
        Confirmed = 2,

        /// <summary>
        /// Usuario rechazó la sugerencia. Puede re-reconciliarse manualmente.
        /// ConfirmedAt y ConfirmedBy registran quién/cuándo rechazó.
        /// </summary>
        Rejected = 3,

        /// <summary>
        /// Usuario marco como revisado.
        /// </summary>
        Reviewed = 4
    }
}
