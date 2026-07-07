using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review;

/// <summary>
/// Detecta grupos de movimientos sospechosos (posibles duplicados, posibles
/// transacciones divididas) dentro de una misma lista de movimientos —
/// un solo lado (Reference o Candidate), no cruza entre ambos.
/// </summary>
public interface ISuspicionDetector
{
    IReadOnlyList<SuspiciousGroup> Detect(IReadOnlyList<FinancialMovement> movements);
}
