using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Contrato de persistencia para ReconciledExpense.
    /// Solo lectura y escritura — sin lógica de negocio.
    /// </summary>
    public interface IReconciledExpenseRepository
    {
        /// <summary>
        /// Persiste un ReconciledExpense con sus Items en una única operación.
        /// expense.Items debe estar cargado antes de llamar.
        /// </summary>
        Task SaveAsync(ReconciledExpense expense, CancellationToken ct = default);

        /// <summary>
        /// Persiste múltiples ReconciledExpenses en una única transacción.
        /// Si falla cualquier insert la transacción entera se revierte.
        /// </summary>
        Task SaveBatchAsync(IReadOnlyList<ReconciledExpense> expenses, CancellationToken ct = default);

        /// <summary>
        /// Devuelve el subconjunto de sourceIds que ya aparece en una
        /// reconciliación activa (Status != Rejected).
        /// Una sola query en lugar de N individuales.
        /// </summary>
        Task<IReadOnlyList<Guid>> GetAlreadyReconciledSourceIdsAsync(
            SourceEntityType sourceType,
            IReadOnlyList<Guid> sourceIds,
            CancellationToken ct = default);

        /// <summary>
        /// Devuelve los expenses del período, opcionalmente filtrados por status.
        /// Incluye Items en la respuesta.
        /// </summary>
        Task<IReadOnlyList<ReconciledExpense>> GetByPeriodAsync(
            DateOnly from,
            DateOnly to,
            ReconciledExpenseStatus? status = null,
            CancellationToken ct = default);
    }
}
