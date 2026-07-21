using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Review.Commands;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

// PR-L4: hasta acá este grupo también exponía /unclassified, /confirm-match,
// /discard-candidates y /restore-candidates — el backend de matching contra
// movimientos legacy que sostenía group-reconciliation.html. Ese mecanismo se retiró
// completo junto con la pantalla (ver ReviewResult.cs para el detalle de por qué).
// /classify sigue siendo el endpoint real de clasificación, usado por movements.html.
public static class MovementReviewEndpoints
{
    public static IEndpointRouteBuilder MapMovementReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/movement-review").WithTags("MovementReview");

        group.MapPost("/classify", Classify);

        return app;
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
            request.Comment,
            request.EffectiveDate);

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
                ClassifyMovementFailureReason.AlreadyPartOfMatchGroup =>
                    Results.Conflict("Este movimiento es parte de un grupo de conciliación (match N↔M); " +
                        "no se puede reclasificar individualmente desde acá"),
                _ => Results.Problem("Error desconocido al clasificar el movimiento"),
            };
        }

        var id = result.ClassifiedMovementId!.Value;
        return Results.Created(
            $"/api/movement-review/{id}",
            new ClassifyMovementResponseDto(id, "Reviewed"));
    }
}
