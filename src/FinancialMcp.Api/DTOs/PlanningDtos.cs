using FinancialSystem.Application.Planning;

namespace FinancialSystem.Api.DTOs;

// ── POST /api/planning-months ───────────────────────────────────────────────

public sealed record CreatePlanningMonthRequest(DateTime Period);

public sealed record PlanningMonthResponseDto(Guid Id, DateTime Period);

// ── POST /api/planning-months/{id}/copy ─────────────────────────────────────

public sealed record CopyPlanningMonthRequest(DateTime TargetPeriod);

public sealed record CopyPlanningMonthResponseDto(Guid Id, DateTime Period, int ItemsCopied);

// ── GET /api/planning-months?period= / /api/planning-months/latest ─────────

public sealed record PlanningItemDto(
    Guid Id, string Title, decimal? ExpectedAmount, DateTime? DueDate, bool IsPaid, DateTime? PaidAt)
{
    public static PlanningItemDto Create(PlanningItemSummary s) => new(
        s.Id, s.Title, s.ExpectedAmount, s.DueDate, s.IsPaid, s.PaidAt);
}

public sealed record PlanningMonthDetailDto(
    Guid Id, DateTime Period, decimal? ExpectedIncome, IReadOnlyList<PlanningItemDto> Items)
{
    public static PlanningMonthDetailDto Create(PlanningMonthDetail d) => new(
        d.Id, d.Period, d.ExpectedIncome, d.Items.Select(PlanningItemDto.Create).ToList());
}

// ── GET /api/planning-months/{id}/summary ───────────────────────────────────

public sealed record PlanningMonthSummaryDto(
    Guid PlanningMonthId,
    DateTime Period,
    decimal? ExpectedIncome,
    decimal TotalPlanned,
    decimal Paid,
    decimal Pending,
    decimal? Available)
{
    public static PlanningMonthSummaryDto Create(PlanningMonthSummary s) => new(
        s.PlanningMonthId, s.Period, s.ExpectedIncome, s.TotalPlanned, s.Paid, s.Pending, s.Available);
}

// ── PUT /api/planning-months/{id}/expected-income ───────────────────────────

public sealed record UpdateExpectedIncomeRequest(decimal? ExpectedIncome);

// ── POST /api/planning-items ─────────────────────────────────────────────────

public sealed record AddPlanningItemRequest(
    Guid PlanningMonthId, string Title, decimal? ExpectedAmount, DateTime? DueDate);

public sealed record PlanningItemResponseDto(Guid Id);

// ── PUT /api/planning-items/{id} ─────────────────────────────────────────────

public sealed record EditPlanningItemRequest(string Title, decimal? ExpectedAmount, DateTime? DueDate);

// ── POST /api/planning-items/{id}/pay ────────────────────────────────────────

public sealed record MarkPlanningItemAsPaidResponseDto(DateTime PaidAt);

// ── GET /api/planning-months/{id}/match-suggestions ─────────────────────────
// Ver docs/Epics/Epica-PlanificacionMensual.md, sección 9. Solo lectura -- la única
// acción que el usuario puede ejecutar a partir de una sugerencia es el POST
// /api/planning-items/{id}/pay ya existente, nunca algo automático acá.

public sealed record PlanningItemMatchSuggestionDto(
    Guid PlanningItemId,
    string PlanningItemTitle,
    Guid ClassifiedMovementId,
    string MovementDescription,
    decimal MovementAmount,
    DateTime MovementEffectiveDate)
{
    public static PlanningItemMatchSuggestionDto Create(PlanningItemMatchSuggestion s) => new(
        s.PlanningItemId, s.PlanningItemTitle, s.ClassifiedMovementId,
        s.MovementDescription, s.MovementAmount, s.MovementEffectiveDate);
}

// ── GET /api/planning-months/dashboard-summary?period= ──────────────────────
// Ver docs/Epics/Epica-PlanificacionMensual.md, sección 8 (Dashboard). Únicamente
// conteos -- sin montos, sin porcentajes, sin comparaciones.

public sealed record PlanningDashboardSummaryDto(
    Guid PlanningMonthId,
    int TotalItems,
    int PendingCount,
    int PaidCount,
    DateTime? NextDueDate)
{
    public static PlanningDashboardSummaryDto Create(PlanningDashboardSummary s) => new(
        s.PlanningMonthId, s.TotalItems, s.PendingCount, s.PaidCount, s.NextDueDate);
}
