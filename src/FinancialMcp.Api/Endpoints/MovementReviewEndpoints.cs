using FinancialSystem.Api.DTOs;
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
}
