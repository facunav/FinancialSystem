using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Punto de entrada principal: recibe movimientos, devuelve resultado.
    /// El motor no sabe de dónde vienen los movimientos ni cómo se persiste.
    /// </summary>
    public interface IReconciliationEngine
    {
        Task<ReconciliationResult> ReconcileAsync(
            ReconciliationRequest request,
            CancellationToken ct = default);
    }
}
