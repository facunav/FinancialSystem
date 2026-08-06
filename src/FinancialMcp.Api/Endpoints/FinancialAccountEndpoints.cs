using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Accounts;
using FinancialSystem.Application.Accounts.Commands;
using FinancialSystem.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Migrado al patrón Command/Query + Handler en PATCH-048 (ver
/// docs/Decisions/ADR-008-command-handler-sin-mediatr.md) -- ya no tiene lógica de
/// negocio propia. GetAll/GetById ya eran delgados desde antes (delegan en
/// IFinancialAccountQueryService, el patrón de servicio de solo lectura que ADR-008
/// reconoce como válido para módulos multi-método, sin cambios en este patch). Create/
/// Update/Deactivate/Reactivate ahora arman el Command correspondiente, lo pasan al
/// Handler de FinancialSystem.Application.Accounts.Commands, y traducen el resultado a
/// una respuesta HTTP -- cada uno sigue validando los mismos parámetros HTTP mínimos
/// que ya validaba antes (Name requerido y Type parseable en Create). Ninguna decisión
/// (unicidad de Name entre cuentas activas, default de Currency, actualización
/// parcial, desactivación/reactivación idempotente) vive acá -- ver los Handlers de
/// Application/Accounts/Commands para esas reglas.
/// </summary>
public static class FinancialAccountEndpoints
{
    public static IEndpointRouteBuilder MapFinancialAccountEndpoints(
        this IEndpointRouteBuilder app)
    {
        // Patch 0060 (PATCH-011): protegido con la misma infraestructura del Patch 0058.
        // Decisión explícita (sección 2 del patch): también se protegen las lecturas, no
        // solo la escritura -- mismo criterio ya aplicado a Importaciones/Movimientos en
        // el Patch 0059 (single-user: leer nombres de cuenta/número de cuenta es tan
        // sensible como poder modificarlos, no hay un usuario "público" legítimo para
        // esta API).
        var group = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/{id:guid}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Deactivate);
        group.MapPost("/{id:guid}/reactivate", Reactivate);

        return app;
    }

    // GET /api/accounts
    private static async Task<IResult> GetAll(
        [FromQuery] bool includeDeactivated = false,
        [FromQuery] string? search = null,
        [FromServices] IFinancialAccountQueryService service = null!,
        CancellationToken ct = default)
    {
        var accounts = await service.GetAllAsync(includeDeactivated, search, ct);
        return Results.Ok(accounts.Select(FinancialAccountDto.Create));
    }

    // GET /api/accounts/{id}
    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] IFinancialAccountQueryService service,
        CancellationToken ct)
    {
        var account = await service.GetByIdAsync(id, ct);
        return account is null ? Results.NotFound() : Results.Ok(FinancialAccountDto.Create(account));
    }

    // POST /api/accounts
    private static async Task<IResult> Create(
        [FromBody] CreateFinancialAccountRequest request,
        [FromServices] CreateFinancialAccountHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("name es requerido");

        if (!Enum.TryParse<FinancialAccountType>(request.Type, ignoreCase: true, out var type))
            return Results.BadRequest($"type inválido: '{request.Type}'");

        var result = await handler.Handle(
            new CreateFinancialAccountCommand(request.Name, type, request.AccountNumber, request.Currency, request.Notes),
            ct);

        if (!result.IsSuccess)
            return Results.Conflict($"Ya existe una cuenta activa con Name='{result.ConflictingName}'");

        return Results.Created($"/api/accounts/{result.Account!.Id}", FinancialAccountDto.Create(result.Account));
    }

    // PUT /api/accounts/{id}
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateFinancialAccountRequest request,
        [FromServices] UpdateFinancialAccountHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(
            new UpdateFinancialAccountCommand(id, request.Name, request.Type, request.AccountNumber, request.Currency, request.Notes),
            ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                UpdateFinancialAccountFailureReason.NotFound => Results.NotFound(),
                UpdateFinancialAccountFailureReason.NameConflict =>
                    Results.Conflict($"Ya existe una cuenta activa con Name='{result.ConflictingName}'"),
                UpdateFinancialAccountFailureReason.InvalidType =>
                    Results.BadRequest($"type inválido: '{result.InvalidTypeValue}'"),
                _ => Results.NotFound(),
            };
        }

        return Results.Ok(FinancialAccountDto.Create(result.Account!));
    }

    // DELETE /api/accounts/{id} — desactiva, no elimina
    private static async Task<IResult> Deactivate(
        Guid id,
        [FromServices] DeactivateFinancialAccountHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DeactivateFinancialAccountCommand(id), ct);
        if (!result.IsSuccess) return Results.NotFound();

        return Results.Ok(new { Message = result.Message });
    }

    // POST /api/accounts/{id}/reactivate
    private static async Task<IResult> Reactivate(
        Guid id,
        [FromServices] ReactivateFinancialAccountHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new ReactivateFinancialAccountCommand(id), ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                ReactivateFinancialAccountFailureReason.NotFound => Results.NotFound(),
                ReactivateFinancialAccountFailureReason.NameConflict => Results.Conflict(result.Message),
                _ => Results.NotFound(),
            };
        }

        return Results.Ok(new { Message = result.Message });
    }
}

public sealed record CreateFinancialAccountRequest(
    string Name,
    string Type,
    string? AccountNumber,
    string? Currency,
    string? Notes);

public sealed record UpdateFinancialAccountRequest(
    string? Name,
    string? Type,
    string? AccountNumber,
    string? Currency,
    string? Notes);
