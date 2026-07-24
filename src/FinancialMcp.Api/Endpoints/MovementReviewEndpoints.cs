using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review.Commands;
using FinancialSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        group.MapGet("/effective-date-suggestion", GetEffectiveDateSuggestion);

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

    // ── GET /api/movement-review/effective-date-suggestion ────────────────────
    //
    // V1 mínima, solo lectura, solo Income: si esta Counterparty tiene al menos 2
    // movimientos Income históricos cuyo EffectiveDate ya fue corregido a mano al
    // mes siguiente de su fecha bancaria real, sugiere el mismo corrimiento (primer
    // día del mes siguiente a `originalDate`) para el movimiento que se está por
    // clasificar. Nunca escribe nada -- movements.html decide si prellenar el campo.
    private static async Task<IResult> GetEffectiveDateSuggestion(
        [FromQuery] Guid counterpartyId,
        [FromQuery] DateTime originalDate,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var incomeMovements = await db.ClassifiedMovements
            .AsNoTracking()
            .Where(m => m.CounterpartyId == counterpartyId && m.FinancialImpact == FinancialImpact.Income)
            .Include(m => m.Items)
            .ToListAsync(ct);

        // Filtro y comparación en memoria (no en la consulta): los grupos N↔M
        // históricos (ConfirmMatchCommand, retirado en PR-L4) no tienen una única
        // OriginalDate inequívoca, y la comparación de mes con acarreo de año no es
        // trivialmente traducible a SQL -- mismo criterio que ya usa
        // FinancialMetricsService.GetPeriodSummaryByAccountAsync para este mismo tipo
        // de acceso a Items.
        var correctedToNextMonth = incomeMovements.Count(m =>
        {
            var references = m.Items.Where(i => i.Role == MovementRole.Reference).ToList();
            if (references.Count != 1) return false;

            var expectedNextMonth = references[0].OriginalDate.AddMonths(1);
            return m.EffectiveDate.Year == expectedNextMonth.Year
                && m.EffectiveDate.Month == expectedNextMonth.Month;
        });

        if (correctedToNextMonth < 2)
            return Results.Ok(new EffectiveDateSuggestionResponse(false, null));

        var nextMonth = originalDate.AddMonths(1);
        var suggested = new DateTime(nextMonth.Year, nextMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return Results.Ok(new EffectiveDateSuggestionResponse(true, suggested));
    }
}
