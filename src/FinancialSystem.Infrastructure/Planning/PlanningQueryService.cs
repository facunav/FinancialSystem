using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Planning;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Planning;

internal sealed class PlanningQueryService : IPlanningQueryService
{
    private readonly IApplicationDbContext _db;

    public PlanningQueryService(IApplicationDbContext db) => _db = db;

    public async Task<PlanningMonthDetail?> GetByPeriodAsync(DateTime period, CancellationToken ct = default)
    {
        var normalizedPeriod = PlanningPeriod.Normalize(period);

        var month = await _db.PlanningMonths
            .AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Period == normalizedPeriod, ct);

        return month is null ? null : ToDetail(month);
    }

    public async Task<PlanningMonthDetail?> GetLatestAsync(CancellationToken ct = default)
    {
        var month = await _db.PlanningMonths
            .AsNoTracking()
            .Include(m => m.Items)
            .OrderByDescending(m => m.Period)
            .FirstOrDefaultAsync(ct);

        return month is null ? null : ToDetail(month);
    }

    public async Task<PlanningMonthSummary?> GetSummaryAsync(Guid planningMonthId, CancellationToken ct = default)
    {
        var month = await _db.PlanningMonths
            .AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Id == planningMonthId, ct);

        if (month is null) return null;

        // Sumas y restas directas sobre ExpectedAmount (épica, sección 6.5) — null
        // (monto todavía no cargado) cuenta como 0, no como una estimación: "todavía
        // no aporta nada al total" es distinto de "el sistema calculó un valor".
        var totalPlanned = month.Items.Sum(i => i.ExpectedAmount ?? 0m);
        var paid = month.Items.Where(i => i.IsPaid).Sum(i => i.ExpectedAmount ?? 0m);
        var pending = totalPlanned - paid;
        var available = month.ExpectedIncome.HasValue
            ? month.ExpectedIncome.Value - totalPlanned
            : (decimal?)null;

        return new PlanningMonthSummary(
            month.Id, month.Period, month.ExpectedIncome, totalPlanned, paid, pending, available);
    }

    public async Task<PlanningDashboardSummary?> GetDashboardSummaryAsync(DateTime period, CancellationToken ct = default)
    {
        var normalizedPeriod = PlanningPeriod.Normalize(period);

        var month = await _db.PlanningMonths
            .AsNoTracking()
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Period == normalizedPeriod, ct);

        if (month is null) return null;

        var pendingDueDates = month.Items
            .Where(i => !i.IsPaid && i.DueDate.HasValue)
            .Select(i => i.DueDate!.Value)
            .ToList();

        var paidCount = month.Items.Count(i => i.IsPaid);

        return new PlanningDashboardSummary(
            month.Id,
            month.Items.Count,
            month.Items.Count - paidCount,
            paidCount,
            pendingDueDates.Count > 0 ? pendingDueDates.Min() : null);
    }

    private static PlanningMonthDetail ToDetail(Domain.Planning.PlanningMonth month) => new(
        month.Id,
        month.Period,
        month.ExpectedIncome,
        month.Items
            .Select(i => new PlanningItemSummary(i.Id, i.Title, i.ExpectedAmount, i.DueDate, i.IsPaid, i.PaidAt))
            .ToList()
            .AsReadOnly());
}
