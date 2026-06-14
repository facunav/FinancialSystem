using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Reconciliation
{
    internal sealed class ReconciledExpenseRepository : IReconciledExpenseRepository
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<ReconciledExpenseRepository> _logger;

        public ReconciledExpenseRepository(
            IApplicationDbContext db,
            ILogger<ReconciledExpenseRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SaveAsync(ReconciledExpense expense, CancellationToken ct = default)
        {
            _db.ReconciledExpenses.Add(expense);
            await _db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "ReconciledExpense persistido: {Id} | {Date:dd/MM/yy} | {Amount} {Currency} | {Items} ítems",
                expense.Id, expense.EffectiveDate,
                expense.TotalAmount, expense.Currency, expense.Items.Count);
        }

        public async Task SaveBatchAsync(
            IReadOnlyList<ReconciledExpense> expenses,
            CancellationToken ct = default)
        {
            if (expenses.Count == 0) return;

            _db.ReconciledExpenses.AddRange(expenses);
            await _db.SaveChangesAsync(ct);

            _logger.LogDebug("Batch de {Count} ReconciledExpenses persistidos", expenses.Count);
        }

        public async Task<IReadOnlyList<Guid>> GetAlreadyReconciledSourceIdsAsync(
            SourceEntityType sourceType,
            IReadOnlyList<Guid> sourceIds,
            CancellationToken ct = default)
        {
            if (sourceIds.Count == 0) return [];

            // Usa el índice IX_ReconciledExpenseItems_Source (SourceEntityType, SourceId).
            // El join implícito al padre filtra Status != Rejected para permitir
            // re-reconciliar movimientos rechazados.
            var already = await _db.ReconciledExpenseItems
                .AsNoTracking()
                .Where(i =>
                    i.SourceEntityType == sourceType &&
                    sourceIds.Contains(i.SourceId) &&
                    i.ReconciledExpense.Status != ReconciledExpenseStatus.Rejected)
                .Select(i => i.SourceId)
                .Distinct()
                .ToListAsync(ct);

            return already.AsReadOnly();
        }

        public async Task<IReadOnlyList<ReconciledExpense>> GetByPeriodAsync(
            DateOnly from,
            DateOnly to,
            ReconciledExpenseStatus? status = null,
            CancellationToken ct = default)
        {
            var query = _db.ReconciledExpenses
                .AsNoTracking()
                .Include(e => e.Items)
                .Where(e => e.PeriodStart >= from && e.PeriodEnd <= to);

            if (status.HasValue)
                query = query.Where(e => e.Status == status.Value);

            var results = await query
                .OrderBy(e => e.EffectiveDate)
                .ToListAsync(ct);

            return results.AsReadOnly();
        }
    }
}
