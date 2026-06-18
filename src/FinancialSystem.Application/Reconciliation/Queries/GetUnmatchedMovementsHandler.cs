using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Reconciliation.Queries
{
    public sealed record GetUnmatchedMovementsQuery(DateOnly From, DateOnly To);

    public sealed record UnmatchedMovementsResult(
        IReadOnlyList<UnmatchedMovementItem> References,
        IReadOnlyList<UnmatchedMovementItem> Candidates);

    public sealed record UnmatchedMovementItem(
        Guid Id,
        string Source,
        DateTime Date,
        string Description,
        decimal Amount,
        string Currency,
        bool AlreadyReconciled);

    public sealed class GetUnmatchedMovementsHandler
    {
        private readonly IApplicationDbContext _db;
        private readonly IManualExpenseRepository _manualExpenses;
        private readonly IReconciledExpenseRepository _reconciledRepo;

        public GetUnmatchedMovementsHandler(
            IApplicationDbContext db,
            IManualExpenseRepository manualExpenses,
            IReconciledExpenseRepository reconciledRepo)
        {
            _db = db;
            _manualExpenses = manualExpenses;
            _reconciledRepo = reconciledRepo;
        }

        public async Task<UnmatchedMovementsResult> Handle(
            GetUnmatchedMovementsQuery query, CancellationToken ct)
        {
            var fromUtc = query.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toUtc = query.To.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            // ── Cargar referencias ─────────────────────────────────────────────
            var transactions = await _db.Transactions
                .AsNoTracking()
                .Where(t => t.Date >= fromUtc && t.Date <= toUtc)
                .ToListAsync(ct);

            var bankStatements = await _db.BankStatements
                .AsNoTracking()
                .Where(b => b.Date >= fromUtc && b.Date <= toUtc)
                .ToListAsync(ct);

            // ── Cargar candidatos ──────────────────────────────────────────────
            var manualExpenses = await _manualExpenses
                .GetByPeriodAsync(query.From, query.To, sheet: null, ct);

            // ── Determinar ya reconciliados — una query por tipo ───────────────
            var txIds = transactions.Select(t => t.Id).ToList();
            var bsIds = bankStatements.Select(b => b.Id).ToList();
            var meIds = manualExpenses.Select(e => e.Id).ToList();

            var reconciledTx = txIds.Count > 0
                ? (await _reconciledRepo.GetAlreadyReconciledSourceIdsAsync(
                    SourceEntityType.Transaction, txIds, ct)).ToHashSet()
                : new HashSet<Guid>();

            var reconciledBs = bsIds.Count > 0
                ? (await _reconciledRepo.GetAlreadyReconciledSourceIdsAsync(
                    SourceEntityType.BankStatement, bsIds, ct)).ToHashSet()
                : new HashSet<Guid>();

            var reconciledMe = meIds.Count > 0
                ? (await _reconciledRepo.GetAlreadyReconciledSourceIdsAsync(
                    SourceEntityType.ManualExpense, meIds, ct)).ToHashSet()
                : new HashSet<Guid>();

            // ── Construir resultado ────────────────────────────────────────────
            var references = transactions
                .Select(t => new UnmatchedMovementItem(
                    t.Id, MovementSource.CreditCard.ToString(),
                    t.Date, t.Description, t.Amount, t.Currency,
                    reconciledTx.Contains(t.Id)))
                .Concat(bankStatements.Select(b => new UnmatchedMovementItem(
                    b.Id, MovementSource.BankDebit.ToString(),
                    b.Date, b.Concept, b.Amount, b.Currency,
                    reconciledBs.Contains(b.Id))))
                .OrderBy(r => r.Date)
                .ToList();

            var candidates = manualExpenses
                .Select(e => new UnmatchedMovementItem(
                    e.Id,
                    e.Sheet == ManualExpenseSheet.Dynamic
                        ? MovementSource.ManualDynamic.ToString()
                        : MovementSource.ManualFixed.ToString(),
                    e.Date, e.Category, e.Amount, e.Currency,
                    reconciledMe.Contains(e.Id)))
                .OrderBy(c => c.Date)
                .ToList();

            return new UnmatchedMovementsResult(references, candidates);
        }
    }
}