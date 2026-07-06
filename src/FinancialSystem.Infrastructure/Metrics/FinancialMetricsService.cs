using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Metrics;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Metrics;

internal sealed class FinancialMetricsService : IFinancialMetricsService
{
    private readonly IApplicationDbContext _db;
    public FinancialMetricsService(IApplicationDbContext db) => _db = db;

    // ── GetPeriodSummaryAsync ─────────────────────────────────────────────────

    public async Task<PeriodSummary> GetPeriodSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc && e.EffectiveDate <= toUtc)
            .Select(e => new RawRow(e.TotalAmount, e.FinancialImpact, e.Status, e.Currency))
            .ToListAsync(ct);

        return BuildSummary(from, to, rows);
    }

    // ── GetExpensesByCategoryAsync ────────────────────────────────────────────

    public async Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        var grouped = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e =>
                e.EffectiveDate >= fromUtc &&
                e.EffectiveDate <= toUtc &&
                e.FinancialImpact == FinancialImpact.Expense)
            .GroupBy(e => new
            {
                e.CategoryId,
                Name = e.Category!.Name,
                DisplayName = e.Category!.DisplayName,
            })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Name,
                g.Key.DisplayName,
                Total = g.Sum(e => e.TotalAmount),
                Count = g.Count(),
            })
            .OrderByDescending(g => g.Total)
            .ToListAsync(ct);

        if (grouped.Count == 0) return [];

        var grandTotal = grouped.Sum(g => g.Total);

        return grouped
            .Select(g => new CategoryExpense(
                g.CategoryId, g.Name, g.DisplayName, g.Total, g.Count,
                grandTotal > 0 ? Math.Round(g.Total / grandTotal * 100, 1) : 0m))
            .ToList()
            .AsReadOnly();
    }

    // ── GetMonthlyTrendAsync ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(
        int months, CancellationToken ct = default)
    {
        if (months <= 0 || months > 36) months = 6;

        var cutoff = DateTime.UtcNow.AddMonths(-months + 1);
        var fromUtc = new DateTime(cutoff.Year, cutoff.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc)
            .Select(e => new { e.EffectiveDate.Year, e.EffectiveDate.Month, e.TotalAmount, e.FinancialImpact })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.Year, r.Month })
            .Select(g =>
            {
                var expenses = g.Where(r => r.FinancialImpact == FinancialImpact.Expense).Sum(r => r.TotalAmount);
                var income = g.Where(r => r.FinancialImpact == FinancialImpact.Income).Sum(r => r.TotalAmount);
                var net = income - expenses;
                var savings = income > 0 ? Math.Round((double)(net / income) * 100, 1) : 0.0;
                return new MonthlyTrendPoint(
                    g.Key.Year, g.Key.Month,
                    MonthLabel(g.Key.Year, g.Key.Month),
                    expenses, income, net, (decimal)savings);
            })
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList()
            .AsReadOnly();
    }

    // ── CompareWithPreviousMonthAsync ─────────────────────────────────────────

    public async Task<MonthComparison> CompareWithPreviousMonthAsync(
        int year, int month, CancellationToken ct = default)
    {
        var currentFrom = new DateOnly(year, month, 1);
        var currentTo = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var prevDate = currentFrom.AddMonths(-1);
        var prevFrom = new DateOnly(prevDate.Year, prevDate.Month, 1);
        var prevTo = new DateOnly(prevDate.Year, prevDate.Month,
                              DateTime.DaysInMonth(prevDate.Year, prevDate.Month));

        var (fromUtc, toUtc) = ToUtcRange(prevFrom, currentTo);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc && e.EffectiveDate <= toUtc)
            .Select(e => new CompareRow(
                e.EffectiveDate, e.TotalAmount, e.FinancialImpact, e.Status, e.Currency,
                e.Category!.DisplayName, e.CategoryId))
            .ToListAsync(ct);

        var currentFromUtc = currentFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var currentRows = rows.Where(r => r.Date >= currentFromUtc).ToList();
        var prevRows = rows.Where(r => r.Date < currentFromUtc).ToList();

        var currentSummary = BuildSummary(currentFrom, currentTo,
            currentRows.Select(r => new RawRow(r.Amount, r.Impact, r.Status, r.Currency)).ToList());
        var previousSummary = prevRows.Count > 0
            ? BuildSummary(prevFrom, prevTo,
                prevRows.Select(r => new RawRow(r.Amount, r.Impact, r.Status, r.Currency)).ToList())
            : (PeriodSummary?)null;

        var expVariation = currentSummary.TotalExpenses - (previousSummary?.TotalExpenses ?? 0m);
        var prevExp = previousSummary?.TotalExpenses ?? 0m;
        var expVariationPct = prevExp > 0 ? Math.Round((double)(expVariation / prevExp) * 100, 1) : 0.0;

        var currByCat = currentRows
            .Where(r => r.Impact == FinancialImpact.Expense)
            .GroupBy(r => new { r.CategoryId, r.CategoryDisplay })
            .ToDictionary(g => g.Key.CategoryId, g => (g.Key.CategoryDisplay, g.Sum(r => r.Amount)));

        var prevByCat = prevRows
            .Where(r => r.Impact == FinancialImpact.Expense)
            .GroupBy(r => new { r.CategoryId, r.CategoryDisplay })
            .ToDictionary(g => g.Key.CategoryId, g => (g.Key.CategoryDisplay, g.Sum(r => r.Amount)));

        var allCats = currByCat.Keys.Union(prevByCat.Keys).ToList();
        var variations = allCats.Select(id =>
        {
            var name = currByCat.TryGetValue(id, out var c)
                ? c.Item1
                : prevByCat.TryGetValue(id, out var p) ? p.Item1 : "?";
            var curr = currByCat.TryGetValue(id, out var cv) ? cv.Item2 : 0m;
            var prev = prevByCat.TryGetValue(id, out var pv) ? pv.Item2 : 0m;
            var variation = curr - prev;
            var pct = prev > 0 ? Math.Round((double)(variation / prev) * 100, 1) : 0.0;
            return new CategoryVariation(name, curr, prev, variation, pct);
        })
        .OrderByDescending(v => Math.Abs(v.Variation))
        .ToList();

        return new MonthComparison(
            currentSummary, previousSummary, expVariation, expVariationPct,
            variations.AsReadOnly());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DateTime fromUtc, DateTime toUtc) ToUtcRange(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

    private static PeriodSummary BuildSummary(DateOnly from, DateOnly to, IReadOnlyList<RawRow> rows)
    {
        var expenses = rows.Where(r => r.Impact == FinancialImpact.Expense).Sum(r => r.Amount);
        var income = rows.Where(r => r.Impact == FinancialImpact.Income).Sum(r => r.Amount);
        var net = income - expenses;
        var savings = income > 0 ? Math.Round((double)(net / income) * 100, 1) : 0.0;
        var currency = rows.Select(r => r.Currency).FirstOrDefault() ?? "ARS";
        return new PeriodSummary(from, to, income, expenses, net, (decimal)savings,
            rows.Count,
            rows.Count(r => r.Status == ClassificationStatus.Confirmed),
            rows.Count(r => r.Status == ClassificationStatus.Reviewed),
            currency);
    }

    private static string MonthLabel(int year, int month)
    {
        var months = new[] { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
        return $"{months[month - 1]} {year}";
    }

    private sealed record RawRow(decimal Amount, FinancialImpact Impact, ClassificationStatus Status, string Currency);
    private sealed record CompareRow(DateTime Date, decimal Amount, FinancialImpact Impact,
        ClassificationStatus Status, string Currency, string CategoryDisplay, Guid CategoryId);
}