using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Investigations.Commands;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

public static class InvestigationEndpoints
{
    public static IEndpointRouteBuilder MapInvestigationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/investigations").WithTags("Investigations");

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
