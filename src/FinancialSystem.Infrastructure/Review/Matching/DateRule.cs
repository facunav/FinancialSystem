using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Review.Matching;

/// <summary>
/// Compara las fechas de dos movimientos. Score 1.0 si coinciden, decreciendo
/// linealmente hasta 0.0 en el límite de la ventana de días configurada.
/// </summary>
internal sealed class DateRule : IMatchingRule
{
    private readonly ReviewEngineOptions _options;

    public DateRule(IOptions<ReviewEngineOptions> options) => _options = options.Value;

    public MatchRuleKind Kind => MatchRuleKind.Date;

    public double Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var daysDiff = Math.Abs((reference.Date.Date - candidate.Date.Date).Days);
        var window = _options.DateWindowDays;

        if (window <= 0)
            return daysDiff == 0 ? 1.0 : 0.0;

        var score = 1.0 - (double)daysDiff / window;
        return Math.Clamp(score, 0.0, 1.0);
    }
}
