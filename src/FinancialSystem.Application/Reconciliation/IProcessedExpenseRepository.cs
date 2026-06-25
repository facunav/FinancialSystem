using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Contrato de persistencia para ProcessedExpense.
    /// Solo lectura y escritura — sin lógica de negocio.
    /// </summary>
    public interface IProcessedExpenseRepository
    {
        /// <summary>
        /// Persiste un ProcessedExpense con sus Items en una única operación.
        /// expense.Items debe estar cargado antes de llamar.
        /// </summary>
        Task SaveAsync(ProcessedExpense expense, CancellationToken ct = default);

        /// <summary>
        /// Persiste múltiples ProcessedExpenses en una única transacción.
        /// Si falla cualquier insert la transacción entera se revierte.
        /// </summary>
        Task SaveBatchAsync(IReadOnlyList<ProcessedExpense> expenses, CancellationToken ct = default);

        /// <summary>
        /// Devuelve el subconjunto de sourceIds que ya aparece en un
        /// ProcessedExpense activo. Una sola query en lugar de N individuales.
        /// </summary>
        Task<IReadOnlyList<Guid>> GetAlreadyProcessedSourceIdsAsync(
            SourceEntityType sourceType,
            IReadOnlyList<Guid> sourceIds,
            CancellationToken ct = default);

        /// <summary>
        /// Devuelve los expenses del período filtrados opcionalmente por status.
        /// Incluye Items en la respuesta.
        /// </summary>
        Task<IReadOnlyList<ProcessedExpense>> GetByPeriodAsync(
            DateTime from,
            DateTime to,
            ExpenseStatus? status = null,
            CancellationToken ct = default);
    }
}
