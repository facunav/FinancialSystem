using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Movements;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Lectura de movimientos de banco/tarjeta para la pantalla Movimientos (Épica K):
/// pendientes y ya clasificados (K3). Depende de IMovementsQueryService, que orquesta
/// IMovementLoader (pendientes) + ClassifiedMovement/ClassifiedMovementItem (clasificados) —
/// esa combinación de dos fuentes es la orquestación real que justifica el servicio
/// (a diferencia de K1, donde una sola fuente + filtrado trivial no lo justificaba).
///
/// Deliberadamente NO usa IReviewEngine: no genera sugerencias, no calcula
/// matching ni sospechosos. Ese es el contrato de /api/movement-review/*, que
/// sigue existiendo sin cambios para la pantalla de Migración desde Excel.
/// </summary>
public static class MovementsEndpoints
{
    public static IEndpointRouteBuilder MapMovementsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/movements").WithTags("Movements");

        group.MapGet("/", GetAll);

        return app;
    }

    // GET /api/movements?from=&to=&financialAccountId=&search=
    private static async Task<IResult> GetAll(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? financialAccountId,
        [FromQuery] string? search,
        [FromServices] IMovementsQueryService movementsQuery,
        CancellationToken ct)
    {
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveFrom = from ?? new DateOnly(effectiveTo.Year, effectiveTo.Month, 1);

        if (effectiveFrom > effectiveTo)
            return Results.BadRequest("'from' debe ser anterior o igual a 'to'");

        var movements = await movementsQuery.GetAsync(effectiveFrom, effectiveTo, financialAccountId, search, ct);

        return Results.Ok(movements.Select(MovementListItemDto.Create));
    }
}
