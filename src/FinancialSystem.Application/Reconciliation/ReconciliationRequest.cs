using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Input del motor de conciliación.
    /// </summary>
    public sealed record ReconciliationRequest
    {
        /// <summary>
        /// Movimientos "de referencia": los que tienen la verdad contable.
        /// Típicamente: movimientos bancarios + tarjetas.
        /// </summary>
        public required IReadOnlyList<FinancialMovement> ReferenceMovements { get; init; }

        /// <summary>
        /// Movimientos "candidatos": los que queremos reconciliar contra la referencia.
        /// Típicamente: gastos manuales.
        /// </summary>
        public required IReadOnlyList<FinancialMovement> CandidateMovements { get; init; }

        public required DateOnly PeriodStart { get; init; }
        public required DateOnly PeriodEnd { get; init; }

        /// <summary>
        /// Opciones que sobreescriben la configuración global para este run.
        /// Si null, se usan las opciones globales inyectadas.
        /// </summary>
        public ReconciliationOptions? Options { get; init; }
    }
}
