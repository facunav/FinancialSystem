using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

public static class CounterPartyEndpoints
{
    public static IEndpointRouteBuilder MapCounterPartyEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/counterparties").WithTags("Counterparties");

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
        [FromServices] IApplicationDbContext db = null!,
        CancellationToken ct = default)
    {
        var query = db.CounterParties
            .AsNoTracking()
            .Include(c => c.DefaultCategory);

        var filtered = includeDeactivated
            ? query.AsQueryable()
            : query.Where(c => !c.IsDeactivated);

        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(c => c.Name.ToLower().Contains(search.ToLower()));

        var results = await filtered
            .OrderBy(c => c.Name)
            .Select(c => ToDto(c))
            .ToListAsync(ct);

        return Results.Ok(results);
    }

    // GET /api/counterparties/{id}
    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var c = await db.CounterParties
            .AsNoTracking()
            .Include(x => x.DefaultCategory)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return c is null ? Results.NotFound() : Results.Ok(ToDto(c));
    }

    // POST /api/counterparties
    private static async Task<IResult> Create(
        [FromBody] CreateCounterPartyRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("name es requerido");

        if (!Enum.TryParse<CounterPartyType>(request.Type, ignoreCase: true, out var type))
            return Results.BadRequest($"type inválido: '{request.Type}'");

        MovementType? defaultMovementType = null;
        if (!string.IsNullOrWhiteSpace(request.DefaultMovementType) &&
            Enum.TryParse<MovementType>(request.DefaultMovementType, ignoreCase: true, out var mt))
            defaultMovementType = mt;

        FinancialImpact? defaultImpact = null;
        if (!string.IsNullOrWhiteSpace(request.DefaultFinancialImpact) &&
            Enum.TryParse<FinancialImpact>(request.DefaultFinancialImpact, ignoreCase: true, out var fi))
            defaultImpact = fi;

        var counterParty = new CounterParty
        {
            Name = request.Name.Trim(),
            Type = type,
            Notes = request.Notes?.Trim(),
            DefaultCategoryId = request.DefaultCategoryId,
            DefaultMovementType = defaultMovementType,
            DefaultFinancialImpact = defaultImpact,
            IsDeactivated = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.CounterParties.Add(counterParty);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/counterparties/{counterParty.Id}",
            new { Id = counterParty.Id, counterParty.Name });
    }

    // PUT /api/counterparties/{id}
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateCounterPartyRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var c = await db.CounterParties.FindAsync([id], ct);
        if (c is null) return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name))
            c.Name = request.Name.Trim();
        if (request.Notes is not null)
            c.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (request.DefaultCategoryId is not null)
            c.DefaultCategoryId = request.DefaultCategoryId;
        if (!string.IsNullOrWhiteSpace(request.DefaultMovementType) &&
            Enum.TryParse<MovementType>(request.DefaultMovementType, ignoreCase: true, out var mt))
            c.DefaultMovementType = mt;
        if (!string.IsNullOrWhiteSpace(request.DefaultFinancialImpact) &&
            Enum.TryParse<FinancialImpact>(request.DefaultFinancialImpact, ignoreCase: true, out var fi))
            c.DefaultFinancialImpact = fi;

        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var updated = await db.CounterParties
            .AsNoTracking()
            .Include(x => x.DefaultCategory)
            .FirstAsync(x => x.Id == id, ct);

        return Results.Ok(ToDto(updated));
    }

    // DELETE /api/counterparties/{id} — desactiva, no elimina
    private static async Task<IResult> Deactivate(
        Guid id,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var c = await db.CounterParties.FindAsync([id], ct);
        if (c is null) return Results.NotFound();
        if (c.IsDeactivated) return Results.Ok(new { Message = "Ya estaba desactivada" });

        c.IsDeactivated = true;
        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { Message = $"Contraparte '{c.Name}' desactivada" });
    }

    private static CounterPartyDto ToDto(CounterParty c) => new(
        c.Id, c.Name, c.Type.ToString(),
        c.DefaultCategoryId,
        c.DefaultCategory?.DisplayName,
        c.DefaultMovementType?.ToString(),
        c.DefaultFinancialImpact?.ToString(),
        c.IsDeactivated);
}

public sealed record CreateCounterPartyRequest(
    string Name,
    string Type,
    string? Notes,
    Guid? DefaultCategoryId,
    string? DefaultMovementType,
    string? DefaultFinancialImpact);

public sealed record UpdateCounterPartyRequest(
    string? Name,
    string? Notes,
    Guid? DefaultCategoryId,
    string? DefaultMovementType,
    string? DefaultFinancialImpact);
