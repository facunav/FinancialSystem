using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Único punto de entrada para persistir conciliaciones confirmadas.
    /// Nada fuera de este servicio llama a IReconciledExpenseRepository.SaveAsync.
    ///
    /// FLUJO:
    ///   1. Validar input (sin I/O) — monedas, IDs vacíos, confirmedBy
    ///   2. Detectar duplicados dentro del batch (sin I/O)
    ///   3. Consultar DB para movimientos ya reconciliados (una query por tipo)
    ///   4. Construir entidades (pure, sin I/O)
    ///   5. Persistir en una única transacción
    /// </summary>
    public sealed class ReconciliationConfirmationService
    {
        private readonly IReconciledExpenseRepository _repository;
        private readonly ILogger<ReconciliationConfirmationService> _logger;

        public ReconciliationConfirmationService(
            IReconciledExpenseRepository repository,
            ILogger<ReconciliationConfirmationService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        // ── Confirmación individual ───────────────────────────────────

        public async Task<ConfirmationResult> ConfirmPairAsync(
       MatchedPair pair,
       string confirmedBy,
       DateOnly periodStart,
       DateOnly periodEnd,
       ConfirmationSource confirmationSource = ConfirmationSource.Manual,
       CancellationToken ct = default)
        {
            var inputError = ValidateInput(pair, confirmedBy);
            if (inputError is not null)
                return ConfirmationResult.Failure(inputError);

            var dbError = await ValidateNotAlreadyReconciledAsync([pair], ct);
            if (dbError is not null)
                return ConfirmationResult.Failure(dbError);

            var expense = BuildExpense(pair, confirmedBy, periodStart, periodEnd, confirmationSource);
            await _repository.SaveAsync(expense, ct);

            _logger.LogInformation(
                "Par confirmado: {ExpenseId} | Ref={RefId} Cand={CandId} | " +
                "Score={Score:P0} Confidence={Confidence} Source={Source} | By={By}",
                expense.Id,
                pair.Reference.Id, pair.Candidate.Id,
                pair.Score.Total, pair.Confidence, confirmationSource, confirmedBy);

            return ConfirmationResult.Success(expense.Id);
        }

        // ── Confirmación en batch ─────────────────────────────────────

        /// <summary>
        /// Valida y confirma múltiples pares en una única transacción.
        ///
        /// Validaciones de negocio: skip individual, el resto continúa.
        /// Errores de DB (SaveBatch): excepción — atómico, ninguno persiste.
        /// </summary>
        public async Task<BatchConfirmationResult> ConfirmBatchAsync(
       IReadOnlyList<PairConfirmationRequest> requests,
       string confirmedBy,
       DateOnly periodStart,
       DateOnly periodEnd,
       CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(confirmedBy))
                return BatchConfirmationResult.AllFailed("confirmedBy no puede estar vacío");

            if (requests.Count == 0)
                return new BatchConfirmationResult([], []);

            var failures = new List<PairFailure>();
            var validReqs = new List<PairConfirmationRequest>();

            foreach (var req in requests)
            {
                var error = ValidateInput(req.Pair, confirmedBy);
                if (error is not null) failures.Add(new PairFailure(req.Pair, error));
                else validReqs.Add(req);
            }

            if (validReqs.Count > 0)
            {
                var pairs = validReqs.Select(r => r.Pair).ToList();
                foreach (var (pair, error) in DetectIntraBatchDuplicates(pairs))
                {
                    var req = validReqs.First(r => r.Pair == pair);
                    failures.Add(new PairFailure(pair, error));
                    validReqs.Remove(req);
                }
            }

            if (validReqs.Count == 0)
                return new BatchConfirmationResult([], failures);

            var validPairs = validReqs.Select(r => r.Pair).ToList();
            var dbError = await ValidateNotAlreadyReconciledAsync(validPairs, ct);
            if (dbError is not null)
            {
                failures.AddRange(validPairs.Select(p => new PairFailure(p, dbError)));
                return new BatchConfirmationResult([], failures);
            }

            var expenses = validReqs
                .Select(r => BuildExpense(
                    r.Pair, confirmedBy, periodStart, periodEnd, r.ConfirmationSource))
                .ToList();

            await _repository.SaveBatchAsync(expenses, ct);

            _logger.LogInformation(
                "Batch confirmado: {Confirmed} persistidos, {Failed} fallidos | By={By}",
                expenses.Count, failures.Count, confirmedBy);

            return new BatchConfirmationResult(
                expenses.Select(e => new ConfirmationSuccess(e.Id)).ToList(),
                failures);
        }

        // ── Validaciones (sin I/O) ────────────────────────────────────

        private static string? ValidateInput(MatchedPair pair, string confirmedBy)
        {
            if (string.IsNullOrWhiteSpace(confirmedBy))
                return "confirmedBy no puede estar vacío";
            if (pair.Reference.Id == Guid.Empty)
                return "El movimiento de referencia no tiene Id válido";
            if (pair.Candidate.Id == Guid.Empty)
                return "El movimiento candidato no tiene Id válido";
            if (pair.Reference.Currency != pair.Candidate.Currency)
                return $"Monedas incompatibles: {pair.Reference.Currency} vs {pair.Candidate.Currency}";
            return null;
        }

        private async Task<string?> ValidateNotAlreadyReconciledAsync(
            IReadOnlyList<MatchedPair> pairs, CancellationToken ct)
        {
            var byType = new Dictionary<SourceEntityType, List<Guid>>();
            foreach (var pair in pairs)
            {
                AddToGroup(byType, SourceTypeOf(pair.Reference.Source), pair.Reference.Id);
                AddToGroup(byType, SourceTypeOf(pair.Candidate.Source), pair.Candidate.Id);
            }

            var conflicts = new List<Guid>();
            foreach (var (sourceType, ids) in byType)
            {
                var already = await _repository.GetAlreadyReconciledSourceIdsAsync(sourceType, ids, ct);
                conflicts.AddRange(already);
            }

            if (conflicts.Count == 0) return null;

            var listed = string.Join(", ", conflicts.Take(5));
            var extra = conflicts.Count > 5 ? $" (y {conflicts.Count - 5} más)" : string.Empty;
            return $"Movimientos ya reconciliados: {listed}{extra}";
        }

        private static List<(MatchedPair Pair, string Error)> DetectIntraBatchDuplicates(
            IReadOnlyList<MatchedPair> pairs)
        {
            var seen = new HashSet<Guid>();
            var duplicates = new List<(MatchedPair, string)>();
            foreach (var pair in pairs)
            {
                if (!seen.Add(pair.Reference.Id))
                    duplicates.Add((pair, $"Movimiento de referencia {pair.Reference.Id} aparece en múltiples pares del batch"));
                else if (!seen.Add(pair.Candidate.Id))
                    duplicates.Add((pair, $"Movimiento candidato {pair.Candidate.Id} aparece en múltiples pares del batch"));
            }
            return duplicates;
        }

        // ── Construcción (pure, sin I/O) ──────────────────────────────

        private static ReconciledExpense BuildExpense(
            MatchedPair pair,
            string confirmedBy,
            DateOnly periodStart,
            DateOnly periodEnd,
            ConfirmationSource confirmationSource)
        {
            var now = DateTime.UtcNow;
            var expense = new ReconciledExpense
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                EffectiveDate = pair.Reference.Date,
                TotalAmount = Math.Abs(pair.Reference.Amount),
                Currency = pair.Reference.Currency,
                Description = BuildDescription(pair.Reference, pair.Candidate),
                Status = ReconciledExpenseStatus.Confirmed,
                MatchScore = pair.Score.Total,
                MatchConfidence = pair.Confidence.ToString(),
                ConfirmationSource = confirmationSource,
                // ── NUEVOS ───────────────────────────────────────────────
                AmountDelta = Math.Abs(Math.Abs(pair.Reference.Amount) - Math.Abs(pair.Candidate.Amount)),
                HasAmountMismatch = false,  // 1↔1 del motor: por definición pasó AmountMatchingRule
                GroupingMode = ReconciliationGroupingMode.EngineSuggested,
                // ─────────────────────────────────────────────────────────
                CreatedAt = now,
                ConfirmedAt = now,
                ConfirmedBy = confirmedBy,
            };

            expense.Items.Add(BuildItem(pair.Reference, ReconciliationItemRole.Reference));
            expense.Items.Add(BuildItem(pair.Candidate, ReconciliationItemRole.Candidate));
            return expense;
        }

        private static ReconciledExpenseItem BuildItem(
            FinancialMovement movement, ReconciliationItemRole role) =>
            new()
            {
                SourceEntityType = SourceTypeOf(movement.Source),
                SourceId = movement.Id,
                Role = role,
                OriginalAmount = movement.Amount,
                OriginalDate = movement.Date,
                OriginalDescription = movement.Description,
                OriginalCurrency = movement.Currency,
                OriginalSourceFile = movement.SourceFile,
            };

        private static string BuildDescription(FinancialMovement reference, FinancialMovement candidate)
        {
            if (reference.Description.Trim().Equals(candidate.Description.Trim(), StringComparison.OrdinalIgnoreCase))
                return reference.Description.Trim();
            return $"{Truncate(candidate.Description.Trim(), 200)} ({Truncate(reference.Description.Trim(), 200)})";
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max] + "…";

        private static SourceEntityType SourceTypeOf(MovementSource source) => source switch
        {
            MovementSource.CreditCard => SourceEntityType.Transaction,
            MovementSource.BankDebit => SourceEntityType.BankStatement,
            MovementSource.ManualDynamic => SourceEntityType.ManualExpense,
            MovementSource.ManualFixed => SourceEntityType.ManualExpense,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        private static void AddToGroup(Dictionary<SourceEntityType, List<Guid>> dict, SourceEntityType key, Guid value)
        {
            if (!dict.TryGetValue(key, out var list)) dict[key] = list = [];
            list.Add(value);
        }
    }

    // ── Wrapper para batch que transporta el ConfirmationSource por par ──

    /// <summary>
    /// Un par + su contexto de confirmación para el batch.
    /// ConfirmationSource viaja por par porque en un mismo batch puede haber
    /// confirmaciones de sugerencias de alta confianza y confirmaciones manuales.
    /// </summary>
    public sealed record PairConfirmationRequest(
        MatchedPair Pair,
        ConfirmationSource ConfirmationSource = ConfirmationSource.Manual);

    // ── Result types ──────────────────────────────────────────────────

    public sealed record ConfirmationResult
    {
        public bool Succeeded { get; init; }
        public Guid? ExpenseId { get; init; }
        public string? ErrorMessage { get; init; }

        public static ConfirmationResult Success(Guid id) => new() { Succeeded = true, ExpenseId = id };
        public static ConfirmationResult Failure(string error) => new() { Succeeded = false, ErrorMessage = error };
    }

    public sealed record BatchConfirmationResult(
        IReadOnlyList<ConfirmationSuccess> Successes,
        IReadOnlyList<PairFailure> Failures)
    {
        public int TotalSucceeded => Successes.Count;
        public int TotalFailed => Failures.Count;
        public bool HasFailures => Failures.Count > 0;

        public static BatchConfirmationResult AllFailed(string error) =>
            new([], [new PairFailure(null!, error)]);
    }

    public sealed record ConfirmationSuccess(Guid ExpenseId);
    public sealed record PairFailure(MatchedPair Pair, string Reason);
}