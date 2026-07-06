using FinancialSystem.Application.Metrics;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/metrics/summary ─────────────────────────────────────────────────

public sealed record PeriodSummaryDto(
    string From,
    string To,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetBalance,
    decimal SavingsRate,
    int ClassifiedCount,
    int ConfirmedCount,
    int ReviewedCount,
    string Currency)
{
    public static PeriodSummaryDto Create(PeriodSummary s) => new(
        s.From.ToString("yyyy-MM-dd"),
        s.To.ToString("yyyy-MM-dd"),
        s.TotalIncome, s.TotalExpenses, s.NetBalance, s.SavingsRate,
        s.ClassifiedCount, s.ConfirmedCount, s.ReviewedCount, s.Currency);
}

// ── GET /api/metrics/by-category ─────────────────────────────────────────────

public sealed record CategoryExpenseDto(
    Guid CategoryId,
    string CategoryName,
    string CategoryDisplayName,
    decimal TotalAmount,
    int TransactionCount,
    decimal PercentageOfTotal)
{
    public static CategoryExpenseDto Create(CategoryExpense c) => new(
        c.CategoryId, c.CategoryName, c.CategoryDisplayName,
        c.TotalAmount, c.TransactionCount, c.PercentageOfTotal);
}

public sealed record CategoryExpensesResponse(
    string From,
    string To,
    decimal GrandTotal,
    IReadOnlyList<CategoryExpenseDto> Categories)
{
    public static CategoryExpensesResponse Create(
        DateOnly from, DateOnly to,
        IReadOnlyList<CategoryExpense> categories) => new(
        from.ToString("yyyy-MM-dd"),
        to.ToString("yyyy-MM-dd"),
        categories.Sum(c => c.TotalAmount),
        categories.Select(CategoryExpenseDto.Create).ToList());
}

// ── GET /api/metrics/monthly-trend ───────────────────────────────────────────

public sealed record MonthlyTrendPointDto(
    int Year,
    int Month,
    string MonthLabel,
    decimal TotalExpenses,
    decimal TotalIncome,
    decimal NetBalance,
    decimal SavingsRate)
{
    public static MonthlyTrendPointDto Create(MonthlyTrendPoint p) => new(
        p.Year, p.Month, p.MonthLabel,
        p.TotalExpenses, p.TotalIncome, p.NetBalance, p.SavingsRate);
}

public sealed record MonthlyTrendResponse(
    int Months,
    IReadOnlyList<MonthlyTrendPointDto> Points)
{
    public static MonthlyTrendResponse Create(int months, IReadOnlyList<MonthlyTrendPoint> points) => new(
        months, points.Select(MonthlyTrendPointDto.Create).ToList());
}

// ── GET /api/metrics/compare ─────────────────────────────────────────────────

public sealed record MonthComparisonDto(
    PeriodSummaryDto Current,
    PeriodSummaryDto? Previous,
    decimal ExpenseVariation,
    double ExpenseVariationPct,
    string ExpenseTrend,
    IReadOnlyList<CategoryVariationDto> CategoryVariations)
{
    public static MonthComparisonDto Create(MonthComparison c) => new(
        Current: PeriodSummaryDto.Create(c.Current),
        Previous: c.Previous is null ? null : PeriodSummaryDto.Create(c.Previous),
        ExpenseVariation: c.ExpenseVariation,
        ExpenseVariationPct: c.ExpenseVariationPct,
        ExpenseTrend: c.ExpenseVariationPct > 2 ? "up" : c.ExpenseVariationPct < -2 ? "down" : "flat",
        CategoryVariations: c.CategoryVariations.Select(CategoryVariationDto.Create).ToList());
}

public sealed record CategoryVariationDto(
    string CategoryDisplayName,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal Variation,
    double VariationPct,
    string Trend)
{
    public static CategoryVariationDto Create(CategoryVariation v) => new(
        v.CategoryDisplayName, v.CurrentAmount, v.PreviousAmount,
        v.Variation, v.VariationPct,
        v.VariationPct > 2 ? "up" : v.VariationPct < -2 ? "down" : "flat");
}