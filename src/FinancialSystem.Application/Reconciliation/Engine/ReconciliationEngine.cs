using System.Diagnostics;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Application.Reconciliation.Matching;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Application.Reconciliation.Engine;

/// <summary>
/// Motor de conciliación financiera.
///
/// ALGORITMO GENERAL (Hungarian-style greedy):
///
///   1. Pre-filtro: descartar pares con hard constraints violadas (moneda)
///   2. Para cada movimiento de referencia, encontrar todos los candidatos
///      dentro de la ventana de fechas y calcular el score compuesto
///   3. Ordenar todos los pares posibles por score descendente
///   4. Asignación greedy: el par con mayor score se confirma primero,
///      y ambos movimientos quedan marcados como "usados"
///   5. Los movimientos sin asignar son "no conciliados"
///   6. Detección de sospechosos (corre sobre los sin-match)
///
/// DECISIÓN GREEDY vs ÓPTIMO:
///   El algoritmo óptimo (Hungarian) es O(n³), demasiado costoso para
///   conjuntos grandes y difícil de debuggear. El greedy es O(n²) y
///   produce resultados idénticos cuando los scores están bien separados
///   (que es el caso de datos financieros reales).
///   Si en el futuro se necesita el óptimo, la interfaz no cambia.
///
/// OBSERVABILIDAD:
///   Todo el proceso está logueado con structured logging para poder
///   diagnosticar matches incorrectos sin modificar el código.
/// </summary>
public sealed class ReconciliationEngine : IReconciliationEngine
{
    private readonly IReadOnlyList<IMatchingRule> _rules;
    private readonly IMatchScorer _scorer;
    private readonly ISuspicionDetector _suspicionDetector;
    private readonly ReconciliationOptions _defaultOptions;
    private readonly ILogger<ReconciliationEngine> _logger;

    public ReconciliationEngine(
        IEnumerable<IMatchingRule> rules,
        IMatchScorer scorer,
        ISuspicionDetector suspicionDetector,
        IOptions<ReconciliationOptions> options,
        ILogger<ReconciliationEngine> logger)
    {
        _rules = rules.ToList().AsReadOnly();
        _scorer = scorer;
        _suspicionDetector = suspicionDetector;
        _defaultOptions = options.Value;
        _logger = logger;
    }

    public Task<ReconciliationResult> ReconcileAsync(
        ReconciliationRequest request,
        CancellationToken ct = default)
    {
        var opts = request.Options ?? _defaultOptions;
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Iniciando conciliación: período {Start} → {End} | " +
            "reference={RefCount} candidate={CandCount} | " +
            "reglas=[{Rules}]",
            request.PeriodStart, request.PeriodEnd,
            request.ReferenceMovements.Count,
            request.CandidateMovements.Count,
            string.Join(", ", _rules.Select(r => $"{r.RuleName}(w={r.Weight:F2})")));

        // ── Paso 1: Detección de sospechosos (pre-matching) ───────
        var allMovements = request.ReferenceMovements
            .Concat(request.CandidateMovements)
            .ToList();
        var suspicious = _suspicionDetector.Detect(allMovements);

        // ── Paso 2: Calcular todos los pares candidatos ───────────
        var allPairs = BuildCandidatePairs(
            request.ReferenceMovements,
            request.CandidateMovements,
            opts);

        _logger.LogDebug("Pares candidatos generados: {Count}", allPairs.Count);

        // ── Paso 3: Asignación greedy ─────────────────────────────
        var (matched, unmatchedRef, unmatchedCand) = GreedyAssign(
            allPairs,
            request.ReferenceMovements,
            request.CandidateMovements,
            opts);

        // ── Paso 4: Near misses para no-conciliados ───────────────
        var unmatchedResult = BuildUnmatched(unmatchedRef, unmatchedCand, allPairs, opts);

        // ── Paso 5: Resultado ────────────────────────────────────
        sw.Stop();
        var summary = BuildSummary(
            request.ReferenceMovements.Count,
            request.CandidateMovements.Count,
            matched, unmatchedResult, suspicious);

        _logger.LogInformation(
            "Conciliación completa en {Elapsed}ms | {Summary}",
            sw.ElapsedMilliseconds, summary);

        return Task.FromResult(new ReconciliationResult
        {
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            Matched = matched,
            Unmatched = unmatchedResult,
            Suspicious = suspicious,
            Summary = summary,
            Elapsed = sw.Elapsed,
        });
    }

    // ── Construcción de pares candidatos ─────────────────────────

    /// <summary>
    /// Genera todos los pares (reference, candidate) que merece la pena evaluar.
    /// Pre-filtra por ventana de fechas para evitar cálculos innecesarios.
    /// </summary>
    private List<MatchedPair> BuildCandidatePairs(
        IReadOnlyList<FinancialMovement> references,
        IReadOnlyList<FinancialMovement> candidates,
        ReconciliationOptions opts)
    {
        var pairs = new List<MatchedPair>();

        foreach (var reference in references)
        {
            // Pre-filtro por ventana de fechas (evita O(n²) completo)
            var windowCandidates = candidates
                .Where(c => Math.Abs((c.Date - reference.Date).TotalDays) <= opts.DateWindowDays)
                .Where(c => string.Equals(c.Currency, reference.Currency, StringComparison.OrdinalIgnoreCase));

            foreach (var candidate in windowCandidates)
            {
                var score = _scorer.Calculate(reference, candidate, _rules);

                if (score.Total < opts.NearMissThreshold) continue;

                var confidence = _scorer.DetermineConfidence(score.Total);
                var contributions = BuildContributions(reference, candidate);

                pairs.Add(new MatchedPair
                {
                    Reference = reference,
                    Candidate = candidate,
                    Score = score,
                    Confidence = confidence,
                    Contributions = contributions,
                });
            }
        }

        return pairs;
    }

    private IReadOnlyList<RuleContribution> BuildContributions(
        FinancialMovement reference,
        FinancialMovement candidate)
    {
        var contributions = new List<RuleContribution>();
        var totalWeight = _rules.Where(r => !r.IsHardConstraint).Sum(r => r.Weight);

        foreach (var rule in _rules)
        {
            var (score, detail) = rule.Evaluate(reference, candidate);
            var normalizedWeight = rule.IsHardConstraint ? 0.0 : rule.Weight / totalWeight;
            contributions.Add(new RuleContribution(rule.RuleName, score, normalizedWeight, detail));
        }

        return contributions.AsReadOnly();
    }

    // ── Asignación greedy ────────────────────────────────────────

    private (
        IReadOnlyList<MatchedPair> Matched,
        List<FinancialMovement> UnmatchedRef,
        List<FinancialMovement> UnmatchedCand
    ) GreedyAssign(
        List<MatchedPair> allPairs,
        IReadOnlyList<FinancialMovement> references,
        IReadOnlyList<FinancialMovement> candidates,
        ReconciliationOptions opts)
    {
        // Ordenar descendente por score — el par más fuerte tiene prioridad
        var sortedPairs = allPairs.OrderByDescending(p => p.Score.Total).ToList();

        var usedReferences = new HashSet<Guid>();
        var usedCandidates = new HashSet<Guid>();
        var matched = new List<MatchedPair>();

        foreach (var pair in sortedPairs)
        {
            // Score mínimo para matchear
            if (pair.Confidence == MatchConfidence.None) break;

            if (usedReferences.Contains(pair.Reference.Id)) continue;
            if (usedCandidates.Contains(pair.Candidate.Id)) continue;

            matched.Add(pair);
            usedReferences.Add(pair.Reference.Id);
            usedCandidates.Add(pair.Candidate.Id);

            _logger.LogDebug(
                "Match: [{RefSource}] {RefDesc} ({RefAmt:N2}) ↔ [{CandSource}] {CandDesc} ({CandAmt:N2}) | {Score}",
                pair.Reference.Source, pair.Reference.Description, pair.Reference.Amount,
                pair.Candidate.Source, pair.Candidate.Description, pair.Candidate.Amount,
                pair.Score);
        }

        var unmatchedRef = references.Where(r => !usedReferences.Contains(r.Id)).ToList();
        var unmatchedCand = candidates.Where(c => !usedCandidates.Contains(c.Id)).ToList();

        _logger.LogInformation(
            "Asignación greedy: {Matched} matches | {UnmatchedRef} ref sin match | {UnmatchedCand} cand sin match",
            matched.Count, unmatchedRef.Count, unmatchedCand.Count);

        return (matched.AsReadOnly(), unmatchedRef, unmatchedCand);
    }

    // ── Construcción de no-conciliados con near misses ────────────

    private List<UnmatchedMovement> BuildUnmatched(
        List<FinancialMovement> unmatchedRef,
        List<FinancialMovement> unmatchedCand,
        List<MatchedPair> allPairs,
        ReconciliationOptions opts)
    {
        var result = new List<UnmatchedMovement>();

        foreach (var movement in unmatchedRef)
        {
            var nearMisses = allPairs
                .Where(p => p.Reference.Id == movement.Id)
                .Where(p => p.Score.Total >= opts.NearMissThreshold)
                .OrderByDescending(p => p.Score.Total)
                .Take(3)
                .ToList();

            var reason = nearMisses.Count > 0
                ? UnmatchedReason.BelowThreshold
                : UnmatchedReason.NoCandidate;

            result.Add(new UnmatchedMovement
            {
                Movement = movement,
                Reason = reason,
                NearMisses = nearMisses.AsReadOnly(),
            });

            if (nearMisses.Count > 0)
            {
                _logger.LogDebug(
                    "Sin match (cerca): {Desc} {Amt:N2} | Mejor candidato: {BestDesc} {BestAmt:N2} score={BestScore:P0}",
                    movement.Description, movement.Amount,
                    nearMisses[0].Candidate.Description, nearMisses[0].Candidate.Amount,
                    nearMisses[0].Score.Total);
            }
        }

        // Los candidatos sin match también se reportan (gastos no registrados en banco)
        foreach (var movement in unmatchedCand)
        {
            result.Add(new UnmatchedMovement
            {
                Movement = movement,
                Reason = UnmatchedReason.NoCandidate,
            });
        }

        return result;
    }

    // ── Summary ──────────────────────────────────────────────────

    private static ReconciliationSummary BuildSummary(
        int totalRef, int totalCand,
        IReadOnlyList<MatchedPair> matched,
        List<UnmatchedMovement> unmatched,
        IReadOnlyList<SuspiciousGroup> suspicious)
    {
        var unmatchedRefAmount = unmatched
            .Where(u => u.Movement.Source is MovementSource.BankDebit or MovementSource.CreditCard)
            .Sum(u => u.Movement.Amount);

        return new ReconciliationSummary
        {
            TotalReferenceMovements = totalRef,
            TotalCandidateMovements = totalCand,
            HighConfidenceMatches = matched.Count(m => m.Confidence == MatchConfidence.High),
            MediumConfidenceMatches = matched.Count(m => m.Confidence == MatchConfidence.Medium),
            LowConfidenceMatches = matched.Count(m => m.Confidence == MatchConfidence.Low),
            UnmatchedReference = unmatched.Count(u => u.Movement.Source is MovementSource.BankDebit or MovementSource.CreditCard),
            UnmatchedCandidate = unmatched.Count(u => u.Movement.Source is MovementSource.ManualDynamic or MovementSource.ManualFixed),
            SuspiciousGroups = suspicious.Count,
            TotalUnmatchedAmount = unmatchedRefAmount,
        };
    }
}
