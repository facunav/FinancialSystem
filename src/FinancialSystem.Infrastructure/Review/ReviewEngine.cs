using System.Diagnostics;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Review;

/// <summary>
/// Orquesta <see cref="IMovementLoader"/>, <see cref="IMatchScorer"/> e
/// <see cref="ISuspicionDetector"/> para un período: separa los movimientos
/// cargados en Reference (banco/tarjeta) y Candidate (legacy), calcula el score
/// de cada par posible, asigna sugerencias 1↔1 por mejor score (greedy) y arma
/// el <see cref="ReviewResult"/> completo.
/// </summary>
internal sealed class ReviewEngine : IReviewEngine
{
    // Tope de "near misses" reportados por movimiento sin sugerencia, para no
    // inflar el resultado con pares de score bajo que no aportan valor de debugging.
    private const int MaxNearMissesPerMovement = 5;

    private readonly IMovementLoader _movementLoader;
    private readonly IMatchScorer _matchScorer;
    private readonly ISuspicionDetector _suspicionDetector;
    private readonly ReviewEngineOptions _options;

    public ReviewEngine(
        IMovementLoader movementLoader,
        IMatchScorer matchScorer,
        ISuspicionDetector suspicionDetector,
        IOptions<ReviewEngineOptions> options)
    {
        _movementLoader = movementLoader;
        _matchScorer = matchScorer;
        _suspicionDetector = suspicionDetector;
        _options = options.Value;
    }

    public async Task<ReviewResult> GenerateAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var movements = await _movementLoader.LoadAsync(from, to, cancellationToken);
        var references = movements.Where(IsReference).ToList();
        var candidates = movements.Where(m => !IsReference(m)).ToList();

        var candidatePairs = ScoreAllPairs(references, candidates);

        var (matched, usedReferenceIds, usedCandidateIds) = AssignMatches(candidatePairs);

        var unmatchedReference = BuildUnmatched(references, usedReferenceIds, candidatePairs,
            p => p.Reference, candidates.Count);
        var unmatchedCandidate = BuildUnmatched(candidates, usedCandidateIds, candidatePairs,
            p => p.Candidate, references.Count);

        var unmatched = new List<UnmatchedMovement>(unmatchedReference.Count + unmatchedCandidate.Count);
        unmatched.AddRange(unmatchedReference);
        unmatched.AddRange(unmatchedCandidate);

        var suspicious = new List<SuspiciousGroup>();
        suspicious.AddRange(_suspicionDetector.Detect(references));
        suspicious.AddRange(_suspicionDetector.Detect(candidates));

        var summary = new ReviewSummary
        {
            TotalReferenceMovements = references.Count,
            TotalCandidateMovements = candidates.Count,
            HighConfidenceMatches = matched.Count(m => m.Confidence == MatchConfidence.High),
            MediumConfidenceMatches = matched.Count(m => m.Confidence == MatchConfidence.Medium),
            LowConfidenceMatches = matched.Count(m => m.Confidence == MatchConfidence.Low),
            UnmatchedReference = unmatchedReference.Count,
            UnmatchedCandidate = unmatchedCandidate.Count,
            SuspiciousGroups = suspicious.Count,
            TotalUnmatchedAmount = unmatchedReference.Sum(u => Math.Abs(u.Movement.Amount)),
        };

        stopwatch.Stop();

        return new ReviewResult
        {
            PeriodStart = from,
            PeriodEnd = to,
            Matched = matched,
            Unmatched = unmatched,
            Suspicious = suspicious,
            Summary = summary,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private static bool IsReference(FinancialMovement movement) =>
        movement.Source is MovementSource.BankDebit or MovementSource.CreditCard;

    /// <summary>Score de cada combinación Reference×Candidate cuya confianza no sea None.</summary>
    private List<CandidatePair> ScoreAllPairs(
        IReadOnlyList<FinancialMovement> references, IReadOnlyList<FinancialMovement> candidates)
    {
        var pairs = new List<CandidatePair>();

        foreach (var reference in references)
        {
            foreach (var candidate in candidates)
            {
                var score = _matchScorer.Score(reference, candidate);
                var confidence = ClassifyConfidence(score.Total);
                if (confidence == MatchConfidence.None) continue;

                pairs.Add(new CandidatePair(reference, candidate, score, confidence));
            }
        }

        return pairs;
    }

    private MatchConfidence ClassifyConfidence(double total)
    {
        if (total >= _options.HighConfidenceThreshold) return MatchConfidence.High;
        if (total >= _options.MediumConfidenceThreshold) return MatchConfidence.Medium;
        if (total >= _options.NearMissThreshold) return MatchConfidence.Low;
        return MatchConfidence.None;
    }

    /// <summary>Asignación greedy 1↔1: mejor score primero, cada movimiento se usa una sola vez.</summary>
    private (List<MatchedPair> Matched, HashSet<Guid> UsedReferenceIds, HashSet<Guid> UsedCandidateIds)
        AssignMatches(List<CandidatePair> candidatePairs)
    {
        var usedReferenceIds = new HashSet<Guid>();
        var usedCandidateIds = new HashSet<Guid>();
        var matched = new List<MatchedPair>();

        foreach (var pair in candidatePairs.OrderByDescending(p => p.Score.Total))
        {
            if (usedReferenceIds.Contains(pair.Reference.Id) || usedCandidateIds.Contains(pair.Candidate.Id))
                continue;

            usedReferenceIds.Add(pair.Reference.Id);
            usedCandidateIds.Add(pair.Candidate.Id);
            matched.Add(ToMatchedPair(pair));
        }

        return (matched, usedReferenceIds, usedCandidateIds);
    }

    private MatchedPair ToMatchedPair(CandidatePair pair) => new()
    {
        Reference = pair.Reference,
        Candidate = pair.Candidate,
        Score = pair.Score,
        Confidence = pair.Confidence,
        Contributions = BuildContributions(pair.Score),
    };

    private List<RuleContribution> BuildContributions(MatchScore score) =>
    [
        new RuleContribution("Amount", score.AmountScore, _options.AmountRuleWeight),
        new RuleContribution("Date", score.DateScore, _options.DateRuleWeight),
        new RuleContribution("Description", score.DescriptionScore, _options.DescriptionRuleWeight),
        new RuleContribution("PaymentMethod", score.PaymentMethodScore, _options.PaymentMethodRuleWeight),
    ];

    /// <summary>
    /// Movimientos del lado indicado que no quedaron asignados en <see cref="AssignMatches"/>,
    /// con su razón y los mejores near-misses (pares descartados por la asignación greedy
    /// o que no llegaron al umbral de "matched").
    /// </summary>
    private List<UnmatchedMovement> BuildUnmatched(
        IReadOnlyList<FinancialMovement> side,
        HashSet<Guid> usedIds,
        List<CandidatePair> candidatePairs,
        Func<CandidatePair, FinancialMovement> selectSide,
        int otherSideCount)
    {
        var result = new List<UnmatchedMovement>();

        foreach (var movement in side)
        {
            if (usedIds.Contains(movement.Id)) continue;

            var nearMisses = candidatePairs
                .Where(p => selectSide(p).Id == movement.Id)
                .OrderByDescending(p => p.Score.Total)
                .Take(MaxNearMissesPerMovement)
                .Select(ToMatchedPair)
                .ToList();

            var reason = otherSideCount == 0
                ? UnmatchedReason.NoCandidate
                : nearMisses.Count == 0
                    ? UnmatchedReason.BelowThreshold
                    : UnmatchedReason.ConflictingMatch;

            result.Add(new UnmatchedMovement
            {
                Movement = movement,
                Reason = reason,
                NearMisses = nearMisses,
            });
        }

        return result;
    }

    private sealed record CandidatePair(
        FinancialMovement Reference,
        FinancialMovement Candidate,
        MatchScore Score,
        MatchConfidence Confidence);
}
