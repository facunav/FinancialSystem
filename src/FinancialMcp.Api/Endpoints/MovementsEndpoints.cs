using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Lectura de movimientos de banco/tarjeta para la futura pantalla Movimientos
/// (Épica K). Depende directamente de IMovementLoader — mismo criterio que
/// MetricsEndpoints con IFinancialMetricsService: sin capa intermedia porque no
/// hay orquestación de negocio, solo filtrado sobre la colección ya cargada.
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
        [FromServices] IMovementLoader movementLoader,
        CancellationToken ct)
    {
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveFrom = from ?? new DateOnly(effectiveTo.Year, effectiveTo.Month, 1);

        if (effectiveFrom > effectiveTo)
            return Results.BadRequest("'from' debe ser anterior o igual a 'to'");

        // Una sola carga en bloque (IMovementLoader ya resuelve todo el período
        // en 3 queries fijas); todo lo que sigue es filtrado en memoria — sin
        // consultas adicionales por movimiento.
        var movements = await movementLoader.LoadAsync(effectiveFrom, effectiveTo, ct);

        var filtered = movements.Where(IsBankOrCard);

        if (financialAccountId is { } accountId)
            filtered = filtered.Where(m => m.FinancialAccountId == accountId);

        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(m => m.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        var result = filtered
            .OrderByDescending(m => m.Date)
            .Select(MovementListItemDto.Create)
            .ToList();

        return Results.Ok(result);
    }

    // Excluye LegacyDynamic/LegacyFixed (LegacyImportedExpense) — esta pantalla
    // es exclusivamente Transaction/BankStatement. Excel queda en Migración desde Excel.
    private static bool IsBankOrCard(FinancialMovement m) =>
        m.Source is MovementSource.BankDebit or MovementSource.CreditCard;
}
