using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Investigations.Commands;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Patch 0061 (PATCH-012): grupo protegido con RequireAuthorization() (mismo esquema
/// "ApiKey" de los Patches 0058/0059/0060) -- iniciar una investigación es una
/// operación de escritura, misma categoría que las ya protegidas en Importaciones/
/// Movimientos (Patch 0059) y datos maestros (Patch 0060).
/// </summary>
public static class InvestigationEndpoints
{
    public static IEndpointRouteBuilder MapInvestigationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/investigations").WithTags("Investigations").RequireAuthorization();

        group.MapPost("/", Create);

        return app;
    }

    // ── POST /api/investigations ────────────────────────────────────────────

    private static async Task<IResult> Create(
        [FromBody] CreateInvestigationRequest request,
        [FromServices] CreateInvestigationHandler handler,
        CancellationToken ct)
    {
        var command = new CreateInvestigationCommand(request.Question, request.Tags);
        var investigation = await handler.Handle(command, ct);

        return Results.Created(
            $"/api/investigations/{investigation.Id}",
            new CreateInvestigationResponseDto(investigation.Id, investigation.Status.ToString(), investigation.CreatedAt));
    }
}
