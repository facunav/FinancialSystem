using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Reconciliation
{
    internal sealed class ProcessedExpenseRepository : IProcessedExpenseRepository
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<ProcessedExpenseRepository> _logger;

        public ProcessedExpenseRepository(
            IApplicationDbContext db,
            ILogger<ProcessedExpenseRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SaveAsync(ProcessedExpense expense, CancellationToken ct = default)
        {
            _db.ProcessedExpenses.Add(expense);
            await _db.SaveChangesAsync(ct);
            _logger.LogDebug(
                "ProcessedExpense persistido: {Id} | {Date:dd/MM/yy} | {Amount} {Currency} | {Items} ítems",
                expense.Id, expense.EffectiveDate, expense.TotalAmount, expense.Currency, expense.Items.Count);
        }

        public async Task SaveBatchAsync(
            IReadOnlyList<ProcessedExpense> expenses,
            CancellationToken ct = default)
        {
            if (expenses.Count == 0) return;
            _db.ProcessedExpenses.AddRange(expenses);
            await _db.SaveChangesAsync(ct);
            _logger.LogDebug("Batch de {Count} ProcessedExpenses persistidos", expenses.Count);
        }

        public async Task<IReadOnlyList<Guid>> GetAlreadyProcessedSourceIdsAsync(
            SourceEntityType sourceType,
            IReadOnlyList<Guid> sourceIds,
            CancellationToken ct = default)
        {
            if (sourceIds.Count == 0) return [];

            // Usa el índice IX_ProcessedExpenseItems_Source (SourceEntityType, SourceId).
            // No hay status "Rejected" en el nuevo modelo — todo ProcessedExpense es verdad financiera.
            var already = await _db.ProcessedExpenseItems
                .AsNoTracking()
                .Where(i =>
                    i.SourceEntityType == sourceType &&
                    sourceIds.Contains(i.SourceId))
                .Select(i => i.SourceId)
                .Distinct()
                .ToListAsync(ct);

            return already.AsReadOnly();
        }

        public async Task<IReadOnlyList<ProcessedExpense>> GetByPeriodAsync(
            DateTime from,
            DateTime to,
            ExpenseStatus? status = null,
            CancellationToken ct = default)
        {
            var query = _db.ProcessedExpenses
                .AsNoTracking()
                .Include(e => e.Items)
                .Include(e => e.Category)
                .Where(e => e.EffectiveDate >= from && e.EffectiveDate <= to);

            if (status.HasValue)
                query = query.Where(e => e.Status == status.Value);

            var results = await query
                .OrderBy(e => e.EffectiveDate)
                .ToListAsync(ct);

            return results.AsReadOnly();
        }
    }

}
