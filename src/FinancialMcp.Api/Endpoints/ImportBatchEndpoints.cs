using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Imports;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

public static class ImportBatchEndpoints
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    public static IEndpointRouteBuilder MapImportBatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        group.MapGet("/history", GetHistory);
        group.MapGet("/{id:guid}", GetById);

        return app;
    }

    // ── GET /api/imports/history?take=50 ──────────────────────────────────────

    private static async Task<IResult> GetHistory(
        [FromQuery] int? take,
        [FromServices] IImportHistoryQueryService service,
        CancellationToken ct)
    {
        var effectiveTake = take.GetValueOrDefault(DefaultTake);
        if (effectiveTake is < 1 or > MaxTake)
            return Results.BadRequest($"'take' debe estar entre 1 y {MaxTake}");

        var batches = await service.GetHistoryAsync(effectiveTake, ct);
        return Results.Ok(ImportHistoryResponse.Create(batches));
    }

    // ── GET /api/imports/{id} ─────────────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] IImportHistoryQueryService service,
        CancellationToken ct)
    {
        var detail = await service.GetByIdAsync(id, ct);
        return detail is null
            ? Results.NotFound($"No existe un ImportBatch con id {id}")
            : Results.Ok(ImportBatchDetailDto.Create(detail));
    }
}
