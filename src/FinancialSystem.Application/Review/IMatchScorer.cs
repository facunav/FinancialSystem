using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review;

/// <summary>
/// Compone las <see cref="IMatchingRule"/> registradas para producir el score total
/// entre un movimiento de referencia y uno candidato.
/// </summary>
public interface IMatchScorer
{
    MatchScore Score(FinancialMovement reference, FinancialMovement candidate);
}
