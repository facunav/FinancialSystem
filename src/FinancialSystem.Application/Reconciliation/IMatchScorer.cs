using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Toma un par (reference, candidate) y los puntajes individuales,
    /// y decide si el par es un match y con qué confianza.
    /// </summary>
    public interface IMatchScorer
    {
        MatchScore Calculate(
            FinancialMovement reference,
            FinancialMovement candidate,
            IReadOnlyList<IMatchingRule> rules);

        MatchConfidence DetermineConfidence(double totalScore);
    }
}
