using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Review.Matching;

/// <summary>
/// Compara los montos absolutos de dos movimientos. Score 1.0 si son iguales,
/// decreciendo linealmente hasta 0.0 en el límite de tolerancia configurado.
/// </summary>
internal sealed class AmountRule : IMatchingRule
{
    private readonly ReviewEngineOptions _options;

    public AmountRule(IOptions<ReviewEngineOptions> options) => _options = options.Value;

    public MatchRuleKind Kind => MatchRuleKind.Amount;

    public double Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var diff = Math.Abs(Math.Abs(reference.Amount) - Math.Abs(candidate.Amount));

        var relativeTolerance = Math.Abs(reference.Amount) * (decimal)_options.AmountRelativeTolerance;
        var tolerance = Math.Max(_options.AmountAbsoluteTolerance, relativeTolerance);

        if (tolerance <= 0)
            return diff == 0 ? 1.0 : 0.0;

        var score = 1.0 - (double)(diff / tolerance);
        return Math.Clamp(score, 0.0, 1.0);
    }
}
