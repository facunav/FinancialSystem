using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Application.Reconciliation.Commands
{
    public sealed record ConfirmGroupCommand(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string ConfirmedBy,
        IReadOnlyList<(Guid Id, MovementSource Source)> ReferenceItems,
        IReadOnlyList<(Guid Id, MovementSource Source)> CandidateItems);

    public sealed record ConfirmGroupResult(
        bool Success,
        Guid? ExpenseId = null,
        decimal ReferenceTotal = 0,
        decimal CandidateTotal = 0,
        decimal AmountDelta = 0,
        bool HasAmountMismatch = false,
        string? Error = null)
    {
        public static ConfirmGroupResult Failure(string error) =>
            new(false, Error: error);

        public static ConfirmGroupResult Ok(
            Guid expenseId, decimal refTotal, decimal candTotal,
            decimal delta, bool mismatch) =>
            new(true, expenseId, refTotal, candTotal, delta, mismatch);
    }

    public sealed class ConfirmGroupHandler
    {
        private readonly IApplicationDbContext _db;
        private readonly IReconciledExpenseRepository _repository;
        private readonly ReconciliationOptions _options;

        public ConfirmGroupHandler(
            IApplicationDbContext db,
            IReconciledExpenseRepository repository,
            IOptions<ReconciliationOptions> options)
        {
            _db = db;
            _repository = repository;
            _options = options.Value;
        }

        public async Task<ConfirmGroupResult> Handle(
            ConfirmGroupCommand cmd, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(cmd.ConfirmedBy))
                return ConfirmGroupResult.Failure("confirmedBy no puede estar vacío");

            if (cmd.ReferenceItems.Count == 0 || cmd.CandidateItems.Count == 0)
                return ConfirmGroupResult.Failure("Se requiere al menos un movimiento en cada lado");

            // ── Cargar movimientos ─────────────────────────────────────────────
            var refMovements = await LoadMovementsAsync(cmd.ReferenceItems, ct);
            var candMovements = await LoadMovementsAsync(cmd.CandidateItems, ct);

            var missingRef = refMovements.IndexOf(null);
            if (missingRef >= 0)
                return ConfirmGroupResult.Failure(
                    $"Movimiento de referencia no encontrado: {cmd.ReferenceItems[missingRef].Id}");

            var missingCand = candMovements.IndexOf(null);
            if (missingCand >= 0)
                return ConfirmGroupResult.Failure(
                    $"Movimiento candidato no encontrado: {cmd.CandidateItems[missingCand].Id}");

            var refs = refMovements.Select(m => m!).ToList();
            var cands = candMovements.Select(m => m!).ToList();

            // ── Validar monedas ────────────────────────────────────────────────
            var currencies = refs.Concat(cands).Select(m => m.Currency).Distinct().ToList();
            if (currencies.Count > 1)
                return ConfirmGroupResult.Failure(
                    $"Monedas inconsistentes en el grupo: {string.Join(", ", currencies)}");

            // ── Validar que ninguno esté ya reconciliado ───────────────────────
            var alreadyError = await ValidateNotAlreadyReconciledAsync(refs, cands, ct);
            if (alreadyError is not null)
                return ConfirmGroupResult.Failure(alreadyError);

            // ── Calcular totales y delta ───────────────────────────────────────
            var refTotal = refs.Sum(m => Math.Abs(m.Amount));
            var candTotal = cands.Sum(m => Math.Abs(m.Amount));
            var delta = Math.Abs(refTotal - candTotal);
            var mismatch = delta > _options.AmountAbsoluteTolerance;

            // ── Construir ReconciledExpense ─────────────────────────────────────
            var now = DateTime.UtcNow;
            var expense = new ReconciledExpense
            {
                PeriodStart = cmd.PeriodStart,
                PeriodEnd = cmd.PeriodEnd,
                EffectiveDate = refs.Min(m => m.Date),
                TotalAmount = refTotal,
                Currency = currencies[0],
                Description = BuildDescription(refs, cands),
                Status = ReconciledExpenseStatus.Confirmed,
                MatchScore = 0.0,
                MatchConfidence = MatchConfidence.None.ToString(),
                ConfirmationSource = ConfirmationSource.Manual,
                AmountDelta = delta,
                HasAmountMismatch = mismatch,
                GroupingMode = ReconciliationGroupingMode.ManualGroup,
                CreatedAt = now,
                ConfirmedAt = now,
                ConfirmedBy = cmd.ConfirmedBy,
            };

            foreach (var m in refs)
                expense.Items.Add(BuildItem(m, ReconciliationItemRole.Reference));
            foreach (var m in cands)
                expense.Items.Add(BuildItem(m, ReconciliationItemRole.Candidate));

            await _repository.SaveAsync(expense, ct);

            return ConfirmGroupResult.Ok(expense.Id, refTotal, candTotal, delta, mismatch);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<List<FinancialMovement?>> LoadMovementsAsync(
            IReadOnlyList<(Guid Id, MovementSource Source)> items,
            CancellationToken ct)
        {
            var result = new List<FinancialMovement?>(items.Count);

            // Agrupar por tipo para hacer una query IN por tipo
            var txIds = items
                .Where(i => i.Source == MovementSource.CreditCard)
                .Select(i => i.Id).ToHashSet();
            var bsIds = items
                .Where(i => i.Source == MovementSource.BankDebit)
                .Select(i => i.Id).ToHashSet();
            var meIds = items
                .Where(i => i.Source is MovementSource.ManualDynamic or MovementSource.ManualFixed)
                .Select(i => i.Id).ToHashSet();

            var txMap = txIds.Count > 0
                ? (await _db.Transactions.AsNoTracking()
                    .Where(t => txIds.Contains(t.Id))
                    .ToListAsync(ct))
                    .ToDictionary(t => t.Id, t => MovementAdapter.FromTransaction(t))
                : new Dictionary<Guid, FinancialMovement>();

            var bsMap = bsIds.Count > 0
                ? (await _db.BankStatements.AsNoTracking()
                    .Where(b => bsIds.Contains(b.Id))
                    .ToListAsync(ct))
                    .ToDictionary(b => b.Id, b => MovementAdapter.FromBankStatement(b))
                : new Dictionary<Guid, FinancialMovement>();

            var meMap = meIds.Count > 0
                ? (await _db.ManualExpenses.AsNoTracking()
                    .Where(m => meIds.Contains(m.Id))
                    .ToListAsync(ct))
                    .ToDictionary(m => m.Id, m => MovementAdapter.FromManualExpense(m))
                : new Dictionary<Guid, FinancialMovement>();

            foreach (var (id, source) in items)
            {
                FinancialMovement? movement = source switch
                {
                    MovementSource.CreditCard => txMap.GetValueOrDefault(id),
                    MovementSource.BankDebit => bsMap.GetValueOrDefault(id),
                    _ => meMap.GetValueOrDefault(id),
                };
                result.Add(movement);
            }

            return result;
        }

        private async Task<string?> ValidateNotAlreadyReconciledAsync(
            List<FinancialMovement> refs,
            List<FinancialMovement> cands,
            CancellationToken ct)
        {
            var txRefIds = refs
                .Where(m => m.Source == MovementSource.CreditCard)
                .Select(m => m.Id).ToList();
            var bsRefIds = refs
                .Where(m => m.Source == MovementSource.BankDebit)
                .Select(m => m.Id).ToList();
            var meCandIds = cands
                .Where(m => m.Source is MovementSource.ManualDynamic or MovementSource.ManualFixed)
                .Select(m => m.Id).ToList();

            var conflicts = new List<Guid>();

            if (txRefIds.Count > 0)
                conflicts.AddRange(await _repository.GetAlreadyReconciledSourceIdsAsync(
                    SourceEntityType.Transaction, txRefIds, ct));
            if (bsRefIds.Count > 0)
                conflicts.AddRange(await _repository.GetAlreadyReconciledSourceIdsAsync(
                    SourceEntityType.BankStatement, bsRefIds, ct));
            if (meCandIds.Count > 0)
                conflicts.AddRange(await _repository.GetAlreadyReconciledSourceIdsAsync(
                    SourceEntityType.ManualExpense, meCandIds, ct));

            if (conflicts.Count == 0) return null;

            return $"Movimientos ya reconciliados: {string.Join(", ", conflicts.Take(5))}" +
                   (conflicts.Count > 5 ? $" (y {conflicts.Count - 5} más)" : string.Empty);
        }

        private static string BuildDescription(
            List<FinancialMovement> refs,
            List<FinancialMovement> cands)
        {
            if (refs.Count == 1 && cands.Count == 1)
            {
                var r = refs[0].Description.Trim();
                var c = cands[0].Description.Trim();
                return r.Equals(c, StringComparison.OrdinalIgnoreCase)
                    ? r
                    : $"{c} ({r})";
            }

            var primary = cands.Count == 1
                ? cands[0].Description.Trim()
                : refs[0].Description.Trim();

            return $"{primary} [{refs.Count}↔{cands.Count}]";
        }

        private static ReconciledExpenseItem BuildItem(
            FinancialMovement m, ReconciliationItemRole role) =>
            new()
            {
                SourceEntityType = m.Source switch
                {
                    MovementSource.CreditCard => SourceEntityType.Transaction,
                    MovementSource.BankDebit => SourceEntityType.BankStatement,
                    _ => SourceEntityType.ManualExpense,
                },
                SourceId = m.Id,
                Role = role,
                OriginalAmount = m.Amount,
                OriginalDate = m.Date,
                OriginalDescription = m.Description,
                OriginalCurrency = m.Currency,
                OriginalSourceFile = m.SourceFile,
            };
    }
}