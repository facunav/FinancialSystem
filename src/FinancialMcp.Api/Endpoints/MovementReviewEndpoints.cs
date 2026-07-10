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
        group.MapPost("/confirm-match", ConfirmMatch);
        group.MapPost("/discard-candidates", DiscardCandidates);
        group.MapPost("/restore-candidates", RestoreCandidates);

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

    // ── POST /api/movement-review/confirm-match ───────────────────────────────

    private static async Task<IResult> ConfirmMatch(
        [FromBody] ConfirmMatchRequest request,
        [FromServices] ConfirmMatchHandler handler,
        CancellationToken ct)
    {
        var command = new ConfirmMatchCommand(
            request.Items
                .Select(i => new ConfirmMatchItem(i.SourceEntityType, i.SourceId, i.Role))
                .ToList(),
            request.CategoryId,
            request.MovementType,
            request.FinancialImpact,
            request.CounterpartyId);

        var result = await handler.Handle(command, ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                ConfirmMatchFailureReason.EmptyItems =>
                    Results.BadRequest("items no puede estar vacío"),
                ConfirmMatchFailureReason.MissingReference =>
                    Results.BadRequest("El grupo debe incluir al menos un item con role=Reference"),
                ConfirmMatchFailureReason.MissingCandidate =>
                    Results.BadRequest("El grupo debe incluir al menos un item con role=Candidate"),
                ConfirmMatchFailureReason.DuplicateItem =>
                    Results.BadRequest("items contiene el mismo sourceEntityType+sourceId repetido"),
                ConfirmMatchFailureReason.RoleSourceMismatch =>
                    Results.BadRequest(
                        $"Role inconsistente con sourceEntityType para: {result.FailureDetail}"),
                ConfirmMatchFailureReason.SourceNotFound =>
                    Results.NotFound($"No existe un movimiento para: {result.FailureDetail}"),
                ConfirmMatchFailureReason.CategoryNotFound =>
                    Results.BadRequest("categoryId no corresponde a ninguna categoría existente"),
                ConfirmMatchFailureReason.CounterpartyNotFound =>
                    Results.BadRequest("counterpartyId no corresponde a ninguna contraparte existente"),
                ConfirmMatchFailureReason.SourceAlreadyClassified =>
                    Results.Conflict($"Ya existe una clasificación para: {result.FailureDetail}"),
                _ => Results.Problem("Error desconocido al confirmar el match"),
            };
        }

        var id = result.ClassifiedMovementId!.Value;
        return Results.Created(
            $"/api/movement-review/{id}",
            new ConfirmMatchResponseDto(id, "Confirmed"));
    }

    // ── POST /api/movement-review/discard-candidates ──────────────────────────

    private static async Task<IResult> DiscardCandidates(
        [FromBody] LegacyCandidatesIdsRequest request,
        [FromServices] DiscardLegacyCandidatesHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DiscardLegacyCandidatesCommand(request.Ids), ct);
        return ToResult(result);
    }

    // ── POST /api/movement-review/restore-candidates ──────────────────────────

    private static async Task<IResult> RestoreCandidates(
        [FromBody] LegacyCandidatesIdsRequest request,
        [FromServices] RestoreLegacyCandidatesHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new RestoreLegacyCandidatesCommand(request.Ids), ct);
        return ToResult(result);
    }

    private static IResult ToResult(LegacyCandidatesBulkResult result)
    {
        if (!result.IsSuccess) return Results.BadRequest("ids no puede estar vacío");

        return Results.Ok(new LegacyCandidatesBulkResponseDto(result.UpdatedIds, result.NotFoundIds));
    }
}
