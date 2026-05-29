namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Rol del ítem dentro de la reconciliación.
    /// </summary>
    public enum ReconciliationItemRole
    {
        /// <summary>Movimiento de verdad contable. Típicamente banco o tarjeta.</summary>
        Reference = 0,

        /// <summary>Movimiento a reconciliar. Típicamente registro manual.</summary>
        Candidate = 1,
    }
}
