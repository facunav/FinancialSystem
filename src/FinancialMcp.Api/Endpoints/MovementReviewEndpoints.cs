using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Review.Commands;
using FinancialSystem.Application.Review.Queries;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

public static class MovementReviewEndpoints
{
    // Límite de rango del período pedido: acota el costo O(Reference × Candidate)
    // del motor de sugerencias, que hoy se recalcula en cada request sin caché.
    private const int MaxDateRangeDays = 90;

    public static IEndpointRouteBuilder MapMovementReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/movement-review").WithTags("MovementReview");

        group.MapGet("/unclassified", GetUnclassified);
        group.MapPost("/classify", Classify);

        return app;
    }

    // ── GET /api/movement-review/unclassified?from=2026-06-01&to=2026-06-30 ──

    private static async Task<IResult> GetUnclassified(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] GetUnclassifiedMovementsHandler handler,
        CancellationToken ct)
    {
        if (from > to) return Results.BadRequest("'from' debe ser anterior o igual a 'to'");

        var rangeDays = to.DayNumber - from.DayNumber + 1;
        if (rangeDays > MaxDateRangeDays)
            return Results.BadRequest($"El rango máximo permitido es de {MaxDateRangeDays} días");

        var result = await handler.Handle(new GetUnclassifiedMovementsQuery(from, to), ct);
        return Results.Ok(ReviewResultDto.Create(result));
    }

    // ── POST /api/movement-review/classify ────────────────────────────────────

    private static async Task<IResult> Classify(
        [FromBody] ClassifyMovementRequest request,
        [FromServices] ClassifyMovementHandler handler,
        CancellationToken ct)
    {
        var command = new ClassifyMovementCommand(
            request.SourceEntityType,
            request.SourceId,
            request.CategoryId,
            request.MovementType,
            request.FinancialImpact,
            request.CounterpartyId,
            request.Comment);

        var result = await handler.Handle(command, ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                ClassifyMovementFailureReason.SourceNotFound =>
                    Results.NotFound("No existe un movimiento con ese sourceEntityType+sourceId"),
                ClassifyMovementFailureReason.CategoryNotFound =>
                    Results.BadRequest("categoryId no corresponde a ninguna categoría existente"),
                ClassifyMovementFailureReason.CounterpartyNotFound =>
                    Results.BadRequest("counterpartyId no corresponde a ninguna contraparte existente"),
                _ => Results.Problem("Error desconocido al clasificar el movimiento"),
            };
        }

        var id = result.ClassifiedMovementId!.Value;
        return Results.Created(
            $"/api/movement-review/{id}",
            new ClassifyMovementResponseDto(id, "Reviewed"));
    }
}
