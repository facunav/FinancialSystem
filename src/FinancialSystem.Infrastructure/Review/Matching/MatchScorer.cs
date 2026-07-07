using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Review.Matching;

/// <summary>
/// Compone las <see cref="IMatchingRule"/> registradas en un <see cref="MatchScore"/>.
/// El total se pondera solo entre las reglas efectivamente registradas, normalizando
/// sus pesos entre sí — así el score queda en [0.0, 1.0] aunque todavía no estén
/// registradas las 4 reglas del modelo completo.
/// </summary>
internal sealed class MatchScorer : IMatchScorer
{
    private readonly IReadOnlyDictionary<MatchRuleKind, IMatchingRule> _rulesByKind;
    private readonly ReviewEngineOptions _options;

    public MatchScorer(IEnumerable<IMatchingRule> rules, IOptions<ReviewEngineOptions> options)
    {
        _rulesByKind = rules.ToDictionary(r => r.Kind);
        _options = options.Value;
    }

    public MatchScore Score(FinancialMovement reference, FinancialMovement candidate)
    {
        var amountScore = Evaluate(MatchRuleKind.Amount, reference, candidate);
        var dateScore = Evaluate(MatchRuleKind.Date, reference, candidate);
        var descriptionScore = Evaluate(MatchRuleKind.Description, reference, candidate);
        var paymentMethodScore = Evaluate(MatchRuleKind.PaymentMethod, reference, candidate);

        var weightedSum = 0.0;
        var totalWeight = 0.0;
        AccumulateWeight(amountScore, _options.AmountRuleWeight, ref weightedSum, ref totalWeight);
        AccumulateWeight(dateScore, _options.DateRuleWeight, ref weightedSum, ref totalWeight);
        AccumulateWeight(descriptionScore, _options.DescriptionRuleWeight, ref weightedSum, ref totalWeight);
        AccumulateWeight(paymentMethodScore, _options.PaymentMethodRuleWeight, ref weightedSum, ref totalWeight);

        return new MatchScore
        {
            AmountScore = amountScore ?? 0.0,
            DateScore = dateScore ?? 0.0,
            DescriptionScore = descriptionScore ?? 0.0,
            PaymentMethodScore = paymentMethodScore ?? 0.0,
            Total = totalWeight > 0 ? weightedSum / totalWeight : 0.0,
        };
    }

    private double? Evaluate(MatchRuleKind kind, FinancialMovement reference, FinancialMovement candidate) =>
        _rulesByKind.TryGetValue(kind, out var rule) ? rule.Evaluate(reference, candidate) : null;

    private static void AccumulateWeight(
        double? score, double weight, ref double weightedSum, ref double totalWeight)
    {
        if (score is null) return;
        weightedSum += weight * score.Value;
        totalWeight += weight;
    }
}
