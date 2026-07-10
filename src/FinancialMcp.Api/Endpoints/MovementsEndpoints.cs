using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Movements;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Lectura de movimientos de banco/tarjeta para la pantalla Movimientos (Épica K):
/// pendientes (con sugerencias del motor, K4, y grupos sospechosos, K6) y ya
/// clasificados (K3). Depende de IMovementsQueryService, que orquesta IReviewEngine
/// (pendientes + sugerencias + sospechosos) + ClassifiedMovement/ClassifiedMovementItem
/// (clasificados) — esa combinación de dos fuentes es la orquestación real que
/// justifica el servicio (a diferencia de K1, donde una sola fuente + filtrado
/// trivial no lo justificaba).
///
/// K4/K6: IMovementsQueryService reutiliza IReviewEngine para sugerencias y
/// sospechosos, no genera matching ni detección propia ni los ejecuta dos veces. No
/// expone confirmación de match N↔M real, resolución de grupos sospechosos, descarte,
/// ni ningún otro elemento de /api/movement-review/*, que sigue existiendo sin cambios
/// para la pantalla de Migración desde Excel.
/// </summary>
public static class MovementsEndpoints
{
    // Mismo límite y misma razón que MovementReviewEndpoints.MaxDateRangeDays: desde
    // K4 este endpoint también dispara el costo O(Reference × Candidate) del motor de
    // sugerencias (antes solo hacía una carga O(N) vía IMovementLoader).
    private const int MaxDateRangeDays = 90;

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

        var rangeDays = effectiveTo.DayNumber - effectiveFrom.DayNumber + 1;
        if (rangeDays > MaxDateRangeDays)
            return Results.BadRequest($"El rango máximo permitido es de {MaxDateRangeDays} días");

        var movements = await movementsQuery.GetAsync(effectiveFrom, effectiveTo, financialAccountId, search, ct);

        return Results.Ok(movements.Select(MovementListItemDto.Create));
    }
}
