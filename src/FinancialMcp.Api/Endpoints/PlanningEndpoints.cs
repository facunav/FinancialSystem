using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Planning;
using FinancialSystem.Application.Planning.Commands;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Expone los casos de uso de FinancialSystem.Application.Planning.Commands +
/// IPlanningQueryService vía Minimal API — ver docs/Epics/Epica-PlanificacionMensual.md.
/// Sin lógica de negocio acá: cada acción solo mapea el request a un Command/Query
/// existente y traduce el resultado a una respuesta HTTP.
///
/// Patch 0046: agrega GetMatchSuggestions (IPlanningMatchSuggestionService, sección 9
/// de la épica) — sigue siendo un GET de solo lectura; la única escritura posible a
/// partir de una sugerencia sigue siendo POST /pay, sin cambios.
/// </summary>
public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        var months = app.MapGroup("/api/planning-months").WithTags("PlanningMonths");

        months.MapPost("/", CreateMonth);
        months.MapGet("/", GetByPeriod);
        months.MapGet("/latest", GetLatest);
        months.MapPost("/{id:guid}/copy", CopyMonth);
        months.MapGet("/{id:guid}/summary", GetSummary);
        months.MapPut("/{id:guid}/expected-income", UpdateExpectedIncome);
        months.MapGet("/{id:guid}/match-suggestions", GetMatchSuggestions);

        var items = app.MapGroup("/api/planning-items").WithTags("PlanningItems");

        items.MapPost("/", AddItem);
        items.MapPut("/{id:guid}", EditItem);
        items.MapDelete("/{id:guid}", DeleteItem);
        items.MapPost("/{id:guid}/pay", MarkAsPaid);
        items.MapPost("/{id:guid}/unpay", UnmarkAsPaid);

        return app;
    }

    // ── POST /api/planning-months ────────────────────────────────────────────

    private static async Task<IResult> CreateMonth(
        [FromBody] CreatePlanningMonthRequest request,
        [FromServices] CreatePlanningMonthHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new CreatePlanningMonthCommand(request.Period), ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                CreatePlanningMonthFailureReason.PeriodAlreadyExists =>
                    Results.Conflict("Ya existe un PlanningMonth para ese período"),
                _ => Results.Problem("Error desconocido al crear el PlanningMonth"),
            };
        }

        return Results.Created(
            $"/api/planning-months/{result.PlanningMonthId}",
            new PlanningMonthResponseDto(result.PlanningMonthId!.Value, result.Period!.Value));
    }

    // ── GET /api/planning-months?period=2026-09-01 ───────────────────────────

    private static async Task<IResult> GetByPeriod(
        [FromQuery] DateTime? period,
        [FromServices] IPlanningQueryService query,
        CancellationToken ct)
    {
        if (period is null)
            return Results.BadRequest("period es requerido");

        var month = await query.GetByPeriodAsync(period.Value, ct);
        return month is null ? Results.NotFound() : Results.Ok(PlanningMonthDetailDto.Create(month));
    }

    // ── GET /api/planning-months/latest ──────────────────────────────────────

    private static async Task<IResult> GetLatest(
        [FromServices] IPlanningQueryService query,
        CancellationToken ct)
    {
        var month = await query.GetLatestAsync(ct);
        return month is null ? Results.NotFound() : Results.Ok(PlanningMonthDetailDto.Create(month));
    }

    // ── POST /api/planning-months/{id}/copy ──────────────────────────────────

    private static async Task<IResult> CopyMonth(
        Guid id,
        [FromBody] CopyPlanningMonthRequest request,
        [FromServices] CopyPlanningMonthHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new CopyPlanningMonthCommand(id, request.TargetPeriod), ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                CopyPlanningMonthFailureReason.SourceNotFound =>
                    Results.NotFound("No existe un PlanningMonth de origen con ese Id"),
                CopyPlanningMonthFailureReason.TargetPeriodAlreadyExists =>
                    Results.Conflict("Ya existe un PlanningMonth para el período de destino"),
                _ => Results.Problem("Error desconocido al copiar el PlanningMonth"),
            };
        }

        return Results.Created(
            $"/api/planning-months/{result.PlanningMonthId}",
            new CopyPlanningMonthResponseDto(
                result.PlanningMonthId!.Value, result.Period!.Value, result.ItemsCopied!.Value));
    }

    // ── GET /api/planning-months/{id}/summary ────────────────────────────────

    private static async Task<IResult> GetSummary(
        Guid id,
        [FromServices] IPlanningQueryService query,
        CancellationToken ct)
    {
        var summary = await query.GetSummaryAsync(id, ct);
        return summary is null ? Results.NotFound() : Results.Ok(PlanningMonthSummaryDto.Create(summary));
    }

    // ── PUT /api/planning-months/{id}/expected-income ────────────────────────

    private static async Task<IResult> UpdateExpectedIncome(
        Guid id,
        [FromBody] UpdateExpectedIncomeRequest request,
        [FromServices] UpdateExpectedIncomeHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new UpdateExpectedIncomeCommand(id, request.ExpectedIncome), ct);
        return result.IsSuccess ? Results.NoContent() : Results.NotFound();
    }

    // ── GET /api/planning-months/{id}/match-suggestions ──────────────────────
    // Ver docs/Epics/Epica-PlanificacionMensual.md, sección 9. Solo lectura: nunca
    // marca nada como pagado por su cuenta -- eso lo sigue haciendo el usuario desde
    // POST /api/planning-items/{id}/pay, ya existente.

    private static async Task<IResult> GetMatchSuggestions(
        Guid id,
        [FromServices] IPlanningMatchSuggestionService matchSuggestions,
        CancellationToken ct)
    {
        var suggestions = await matchSuggestions.GetSuggestionsAsync(id, ct);
        return suggestions is null
            ? Results.NotFound()
            : Results.Ok(suggestions.Select(PlanningItemMatchSuggestionDto.Create));
    }

    // ── POST /api/planning-items ──────────────────────────────────────────────

    private static async Task<IResult> AddItem(
        [FromBody] AddPlanningItemRequest request,
        [FromServices] AddPlanningItemHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Results.BadRequest("title es requerido");

        var command = new AddPlanningItemCommand(
            request.PlanningMonthId, request.Title, request.ExpectedAmount, request.DueDate);
        var result = await handler.Handle(command, ct);

        if (!result.IsSuccess)
            return Results.NotFound("No existe un PlanningMonth con ese Id");

        return Results.Created(
            $"/api/planning-items/{result.PlanningItemId}",
            new PlanningItemResponseDto(result.PlanningItemId!.Value));
    }

    // ── PUT /api/planning-items/{id} ──────────────────────────────────────────

    private static async Task<IResult> EditItem(
        Guid id,
        [FromBody] EditPlanningItemRequest request,
        [FromServices] EditPlanningItemHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Results.BadRequest("title es requerido");

        var command = new EditPlanningItemCommand(id, request.Title, request.ExpectedAmount, request.DueDate);
        var result = await handler.Handle(command, ct);
        return result.IsSuccess ? Results.NoContent() : Results.NotFound();
    }

    // ── DELETE /api/planning-items/{id} ───────────────────────────────────────

    private static async Task<IResult> DeleteItem(
        Guid id,
        [FromServices] DeletePlanningItemHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DeletePlanningItemCommand(id), ct);
        return result.IsSuccess ? Results.NoContent() : Results.NotFound();
    }

    // ── POST /api/planning-items/{id}/pay ─────────────────────────────────────

    private static async Task<IResult> MarkAsPaid(
        Guid id,
        [FromServices] MarkPlanningItemAsPaidHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new MarkPlanningItemAsPaidCommand(id), ct);
        return result.IsSuccess
            ? Results.Ok(new MarkPlanningItemAsPaidResponseDto(result.PaidAt!.Value))
            : Results.NotFound();
    }

    // ── POST /api/planning-items/{id}/unpay ───────────────────────────────────

    private static async Task<IResult> UnmarkAsPaid(
        Guid id,
        [FromServices] UnmarkPlanningItemAsPaidHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new UnmarkPlanningItemAsPaidCommand(id), ct);
        return result.IsSuccess ? Results.NoContent() : Results.NotFound();
    }
}
