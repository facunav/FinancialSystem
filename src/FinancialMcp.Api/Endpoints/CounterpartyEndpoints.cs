using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Counterparties;
using FinancialSystem.Application.Counterparties.Commands;
using FinancialSystem.Application.Counterparties.Queries;
using FinancialSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Migrado al patrón Command/Query + Handler en PATCH-047 (ver
/// docs/Decisions/ADR-008-command-handler-sin-mediatr.md) -- ya no tiene lógica de
/// negocio propia: cada acción valida parámetros HTTP mínimos (Name requerido y Type
/// parseable en Create -- las dos únicas validaciones de forma que existían), arma el
/// Command/Query correspondiente, lo pasa al Handler de
/// FinancialSystem.Application.Counterparties, y traduce el resultado a una respuesta
/// HTTP. Ninguna decisión (default de Type a Other, parseo best-effort de
/// DefaultMovementType/DefaultFinancialImpact, actualización parcial, desactivación
/// idempotente) vive acá -- ver los Handlers de Application/Counterparties para esas
/// reglas.
/// </summary>
public static class CounterpartyEndpoints
{
    public static IEndpointRouteBuilder MapCounterpartyEndpoints(
        this IEndpointRouteBuilder app)
    {
        // Patch 0060 (PATCH-011): protegido con la misma infraestructura del Patch 0058,
        // incluidas las lecturas -- ver el comentario equivalente en
        // FinancialAccountEndpoints para la justificación de la decisión.
        var group = app.MapGroup("/api/counterparties").WithTags("Counterparties").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/{id:guid}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Deactivate);

        return app;
    }

    // GET /api/counterparties
    private static async Task<IResult> GetAll(
        [FromQuery] bool includeDeactivated = false,
        [FromQuery] string? search = null,
        [FromServices] GetCounterpartiesHandler handler = null!,
        CancellationToken ct = default)
    {
        var counterparties = await handler.Handle(new GetCounterpartiesQuery(includeDeactivated, search), ct);

        return Results.Ok(counterparties.Select(ToDto).ToList());
    }

    // GET /api/counterparties/{id}
    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] GetCounterpartyByIdHandler handler,
        CancellationToken ct)
    {
        var counterparty = await handler.Handle(new GetCounterpartyByIdQuery(id), ct);

        return counterparty is null ? Results.NotFound() : Results.Ok(ToDto(counterparty));
    }

    // POST /api/counterparties
    private static async Task<IResult> Create(
        [FromBody] CreateCounterpartyRequest request,
        [FromServices] CreateCounterpartyHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("name es requerido");

        // Type ya no es obligatorio en el alta (ver análisis de la entidad Counterparty):
        // no tiene ningún consumidor hoy fuera de esta validación. El default a Other
        // cuando no se informa es una decisión de negocio -- se resuelve en el Handler.
        CounterpartyType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.TryParse<CounterpartyType>(request.Type, ignoreCase: true, out var parsedType))
                return Results.BadRequest($"type inválido: '{request.Type}'");
            type = parsedType;
        }

        var counterparty = await handler.Handle(
            new CreateCounterpartyCommand(
                request.Name, type, request.Notes, request.DefaultCategoryId,
                request.DefaultMovementType, request.DefaultFinancialImpact),
            ct);

        return Results.Created(
            $"/api/counterparties/{counterparty.Id}",
            new { Id = counterparty.Id, counterparty.Name });
    }

    // PUT /api/counterparties/{id}
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateCounterpartyRequest request,
        [FromServices] UpdateCounterpartyHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(
            new UpdateCounterpartyCommand(
                id, request.Name, request.Notes, request.DefaultCategoryId,
                request.DefaultMovementType, request.DefaultFinancialImpact),
            ct);

        if (!result.IsSuccess) return Results.NotFound();

        return Results.Ok(ToDto(result.Counterparty!));
    }

    // DELETE /api/counterparties/{id} — desactiva, no elimina
    private static async Task<IResult> Deactivate(
        Guid id,
        [FromServices] DeactivateCounterpartyHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DeactivateCounterpartyCommand(id), ct);
        if (!result.IsSuccess) return Results.NotFound();

        return Results.Ok(new { Message = result.Message });
    }

    private static CounterpartyDto ToDto(CounterpartySummary c) => new(
        c.Id, c.Name, c.Type,
        c.DefaultCategoryId, c.DefaultCategoryName,
        c.DefaultMovementType, c.DefaultFinancialImpact,
        c.IsDeactivated);
}

public sealed record CreateCounterpartyRequest(
    string Name,
    string? Type,
    string? Notes,
    Guid? DefaultCategoryId,
    string? DefaultMovementType,
    string? DefaultFinancialImpact);

public sealed record UpdateCounterpartyRequest(
    string? Name,
    string? Notes,
    Guid? DefaultCategoryId,
    string? DefaultMovementType,
    string? DefaultFinancialImpact);
