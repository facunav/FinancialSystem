using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Detecta movimientos sospechosos (duplicados, splits) ANTES del matching.
    /// Opera sobre la lista completa de movimientos de una misma fuente.
    /// </summary>
    public interface ISuspicionDetector
    {
        IReadOnlyList<SuspiciousGroup> Detect(IReadOnlyList<FinancialMovement> movements);
    }
}
