using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics").WithTags("Metrics");

        group.MapGet("/summary", GetSummary);
        group.MapGet("/by-category", GetByCategory);
        group.MapGet("/monthly-trend", GetMonthlyTrend);
        group.MapGet("/compare", GetComparison);

        return app;
    }

    // ── GET /api/metrics/summary?year=2026&month=6 ────────────────────────────
    // o bien: ?from=2026-01-01&to=2026-06-30 para períodos arbitrarios

    private static async Task<IResult> GetSummary(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IFinancialMetricsService metrics,
        CancellationToken ct)
    {
        var (f, t) = ResolvePeriod(year, month, from, to);
        if (f is null || t is null)
            return Results.BadRequest("Indicá year+month o from+to");

        var summary = await metrics.GetPeriodSummaryAsync(f.Value, t.Value, ct);
        return Results.Ok(PeriodSummaryDto.Create(summary));
    }

    // ── GET /api/metrics/by-category?from=2026-06-01&to=2026-06-30 ───────────

    private static async Task<IResult> GetByCategory(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] IFinancialMetricsService metrics,
        CancellationToken ct)
    {
        if (from >= to) return Results.BadRequest("'from' debe ser anterior a 'to'");

        var categories = await metrics.GetExpensesByCategoryAsync(from, to, ct);
        return Results.Ok(CategoryExpensesResponse.Create(from, to, categories));
    }

    // ── GET /api/metrics/monthly-trend?months=6 ───────────────────────────────

    private static async Task<IResult> GetMonthlyTrend(
        [FromQuery] int months,
        [FromServices] IFinancialMetricsService metrics,
        CancellationToken ct)
    {
        if (months is < 1 or > 36)
            return Results.BadRequest("months debe estar entre 1 y 36");

        var trend = await metrics.GetMonthlyTrendAsync(months, ct);
        return Results.Ok(MonthlyTrendResponse.Create(months, trend));
    }

    // ── GET /api/metrics/compare?year=2026&month=6 ────────────────────────────

    private static async Task<IResult> GetComparison(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromServices] IFinancialMetricsService metrics,
        CancellationToken ct)
    {
        if (month is < 1 or > 12) return Results.BadRequest("month debe estar entre 1 y 12");
        if (year < 2000 || year > 2100) return Results.BadRequest("year inválido");

        var comparison = await metrics.CompareWithPreviousMonthAsync(year, month, ct);
        return Results.Ok(MonthComparisonDto.Create(comparison));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static (DateOnly? from, DateOnly? to) ResolvePeriod(
        int? year, int? month, DateOnly? from, DateOnly? to)
    {
        if (year.HasValue && month.HasValue && month.Value is >= 1 and <= 12)
        {
            var f = new DateOnly(year.Value, month.Value, 1);
            var t = new DateOnly(year.Value, month.Value, DateTime.DaysInMonth(year.Value, month.Value));
            return (f, t);
        }
        if (from.HasValue && to.HasValue && from < to)
            return (from, to);
        return (null, null);
    }
}