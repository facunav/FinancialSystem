using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Application.Reconciliation.Matching;

/// <summary>
/// Calcula el score compuesto ponderado a partir de los scores individuales.
///
/// ALGORITMO:
///   1. Para cada regla, llamar Evaluate()
///   2. Si es HardConstraint y score == 0 → retornar score total = 0 inmediatamente
///   3. Normalizar pesos de reglas no-constraint para que sumen 1.0
///   4. Score total = suma(score_i * peso_normalizado_i)
///   5. Determinar confianza según umbrales configurados
///
/// TRANSPARENCIA:
///   Cada RuleContribution queda registrado para poder explicar
///   por qué un par matcheó o no. Crítico para debugging.
/// </summary>
public sealed class MatchScorer : IMatchScorer
{
    private readonly ReconciliationOptions _opts;

    public MatchScorer(IOptions<ReconciliationOptions> opts) => _opts = opts.Value;

    public MatchScore Calculate(
        FinancialMovement reference,
        FinancialMovement candidate,
        IReadOnlyList<IMatchingRule> rules)
    {
        var contributions = new List<RuleContribution>(rules.Count);
        var scoringRules = new List<(IMatchingRule Rule, double Score, string? Detail)>();

        // ── Paso 1: evaluar todas las reglas ─────────────────────
        foreach (var rule in rules)
        {
            var (score, detail) = rule.Evaluate(reference, candidate);

            // Hard constraint: si falla, el par queda inmediatamente descalificado
            if (rule.IsHardConstraint && score == 0.0)
            {
                contributions.Add(new RuleContribution(rule.RuleName, 0.0, 0.0, detail ?? "Constraint violado"));
                return BuildZeroScore(contributions, detail ?? $"{rule.RuleName}: constraint violado");
            }

            if (!rule.IsHardConstraint)
                scoringRules.Add((rule, score, detail));
        }

        // ── Paso 2: normalizar pesos ──────────────────────────────
        var totalWeight = scoringRules.Sum(r => r.Rule.Weight);
        if (totalWeight == 0.0)
            return BuildZeroScore(contributions, "Sin reglas con peso > 0");

        // ── Paso 3: score ponderado ───────────────────────────────
        var weightedSum = 0.0;
        foreach (var (rule, score, detail) in scoringRules)
        {
            var normalizedWeight = rule.Weight / totalWeight;
            weightedSum += score * normalizedWeight;
            contributions.Add(new RuleContribution(rule.RuleName, score, normalizedWeight, detail));
        }

        var total = Math.Clamp(weightedSum, 0.0, 1.0);

        return new MatchScore
        {
            AmountScore = GetScoreFor("Amount", scoringRules),
            DateScore = GetScoreFor("Date", scoringRules),
            DescriptionScore = GetScoreFor("Description", scoringRules),
            PaymentMethodScore = GetScoreFor("PaymentMethod", scoringRules),
            Total = total,
        };
    }

    public MatchConfidence DetermineConfidence(double totalScore)       
    {
        if (double.IsNaN(totalScore))
            return MatchConfidence.None;

        if (totalScore >= _opts.HighConfidenceThreshold)
            return MatchConfidence.High;

        if (totalScore >= _opts.MediumConfidenceThreshold)
            return MatchConfidence.Medium;

        if (totalScore >= _opts.NearMissThreshold)
            return MatchConfidence.Low;

        return MatchConfidence.None;
    }

    private static MatchScore BuildZeroScore(List<RuleContribution> contributions, string reason)
    {
        return new MatchScore
        {
            AmountScore = 0, DateScore = 0,
            DescriptionScore = 0, PaymentMethodScore = 0,
            Total = 0,
        };
    }

    private static double GetScoreFor(
        string ruleName,
        List<(IMatchingRule Rule, double Score, string? Detail)> rules)
        => rules.FirstOrDefault(r => r.Rule.RuleName == ruleName).Score;
}
