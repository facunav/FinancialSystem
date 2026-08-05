using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Metrics;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Metrics;

internal sealed class FinancialMetricsService : IFinancialMetricsService
{
    private readonly IApplicationDbContext _db;
    private readonly IMovementsQueryService _movementsQuery;
    private readonly ILogger<FinancialMetricsService> _logger;

    public FinancialMetricsService(
        IApplicationDbContext db,
        IMovementsQueryService movementsQuery,
        ILogger<FinancialMetricsService> logger)
    {
        _db = db;
        _movementsQuery = movementsQuery;
        _logger = logger;
    }

    // ── GetPeriodSummaryAsync ─────────────────────────────────────────────────

    public async Task<PeriodSummary> GetPeriodSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        // INSTRUMENTACIÓN TEMPORAL (dashboard-income-wrong-month): rango exacto
        // (con precisión de tick, formato "O") que se manda al filtro EF Core.
        _logger.LogInformation(
            "GetPeriodSummaryAsync: from={From} to={To} fromUtc={FromUtc:O} toUtc={ToUtc:O}",
            from, to, fromUtc, toUtc);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc && e.EffectiveDate <= toUtc)
            .Select(e => new RawRow(e.TotalAmount, e.FinancialImpact, e.Status, e.Currency))
            .ToListAsync(ct);

        // Diagnóstico de borde: trae (sin filtrar por el rango del summary) todo lo
        // que esté a +/-2 días de cada límite, para ver el EffectiveDate exacto que
        // Postgres devuelve y si cae de un lado u otro del corte -- incluye tanto lo
        // que matcheó como lo que quedó justo afuera, para comparar contra fromUtc/toUtc.
        var boundaryRows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e =>
                (e.EffectiveDate >= fromUtc.AddDays(-2) && e.EffectiveDate <= fromUtc.AddDays(2)) ||
                (e.EffectiveDate >= toUtc.AddDays(-2) && e.EffectiveDate <= toUtc.AddDays(2)))
            .Select(e => new { e.Id, e.EffectiveDate, e.FinancialImpact, e.TotalAmount })
            .ToListAsync(ct);

        foreach (var r in boundaryRows)
        {
            var includedInRange = r.EffectiveDate >= fromUtc && r.EffectiveDate <= toUtc;
            _logger.LogInformation(
                "GetPeriodSummaryAsync: boundary row Id={Id} EffectiveDate={EffectiveDate:O} " +
                "Impact={Impact} Amount={Amount} includedInRange={Included}",
                r.Id, r.EffectiveDate, r.FinancialImpact, r.TotalAmount, includedInRange);
        }

        var summary = BuildSummary(from, to, rows);
        _logger.LogInformation(
            "GetPeriodSummaryAsync: result rowCount={RowCount} totalIncome={TotalIncome} totalExpenses={TotalExpenses}",
            rows.Count, summary.TotalIncome, summary.TotalExpenses);

        return summary;
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

    // ── GetClassificationCoverageAsync ────────────────────────────────────────
    // Patch 0068 (PATCH-019), Épica L: reutiliza IMovementsQueryService.GetAsync --
    // la misma fuente que ya usa la pantalla Movimientos -- en vez de una consulta
    // propia contra ClassifiedMovements/Transactions/BankStatements por separado, para
    // no duplicar la lógica de "pendiente vs. clasificado" (unión banco+tarjeta,
    // resolución de ClassifiedMovementItem) que esa clase ya resuelve. Determinístico
    // para un mismo período: sin aleatoriedad ni dependencia de la hora de ejecución
    // más allá del contenido de la base en el momento de la consulta.

    public async Task<ClassificationCoverage> GetClassificationCoverageAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var movements = await _movementsQuery.GetAsync(
            from, to, financialAccountId: null, search: null, ct);

        var total = movements.Count;
        var classified = movements.Count(m => m.Status is not null);
        var coveragePercentage = total > 0
            ? Math.Round((decimal)classified / total * 100, 1)
            : 0m;

        return new ClassificationCoverage(from, to, total, classified, coveragePercentage);
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