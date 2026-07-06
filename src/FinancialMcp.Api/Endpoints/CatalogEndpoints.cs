using FinancialMcp.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

// ── CATEGORÍAS ────────────────────────────────────────────────────────────────

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").WithTags("Categories");

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Deactivate);

        return app;
    }

    // GET /api/categories — devuelve activas (excluye desactivadas por defecto)
    private static async Task<IResult> GetAll(
        [FromQuery] bool includeDeactivated = false,
        [FromServices] IApplicationDbContext db = null!,
        CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking();
        if (!includeDeactivated)
            query = query.Where(c => !c.IsDeactivated);

        var categories = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.DisplayName)
            .Select(c => new CategoryDto(c.Id, c.Name, c.DisplayName, c.SortOrder, c.IsDeactivated))
            .ToListAsync(ct);

        return Results.Ok(categories);
    }

    // POST /api/categories
    private static async Task<IResult> Create(
        [FromBody] CreateCategoryRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest("displayName es requerido");

        // Name se deriva del DisplayName normalizado si no se provee
        var name = string.IsNullOrWhiteSpace(request.Name)
            ? NormalizeName(request.DisplayName)
            : request.Name.Trim();

        var exists = await db.Categories.AnyAsync(c => c.Name == name, ct);
        if (exists)
            return Results.Conflict($"Ya existe una categoría con Name='{name}'");

        var maxSort = await db.Categories.AsNoTracking()
            .MaxAsync(c => (int?)c.SortOrder, ct) ?? 0;

        var category = new Category
        {
            Name = name,
            DisplayName = request.DisplayName.Trim(),
            SortOrder = maxSort + 10,
            IsSystem = false,
            IsDeactivated = false,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/categories/{category.Id}",
            new CategoryDto(category.Id, category.Name, category.DisplayName,
                category.SortOrder, category.IsDeactivated));
    }

    // PUT /api/categories/{id}
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null) return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            category.DisplayName = request.DisplayName.Trim();

        if (request.SortOrder.HasValue)
            category.SortOrder = request.SortOrder.Value;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new CategoryDto(category.Id, category.Name,
            category.DisplayName, category.SortOrder, category.IsDeactivated));
    }

    // DELETE /api/categories/{id} — desactiva, no elimina
    private static async Task<IResult> Deactivate(
        Guid id,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null) return Results.NotFound();
        if (category.IsSystem)
            return Results.BadRequest("Las categorías del sistema no pueden desactivarse");
        if (category.IsDeactivated)
            return Results.Ok(new { Message = "Ya estaba desactivada" });

        category.IsDeactivated = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { Message = $"Categoría '{category.DisplayName}' desactivada" });
    }

    private static string NormalizeName(string displayName) =>
        new string(displayName.Trim()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Replace(" ", "");
}

public sealed record CreateCategoryRequest(string DisplayName, string? Name);
public sealed record UpdateCategoryRequest(string? DisplayName, int? SortOrder);

// ── CONTRAPARTES ──────────────────────────────────────────────────────────────

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
        [FromBody] CreateCounterpartyRequest request,
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

        var counterparty = new CounterParty
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

        db.CounterParties.Add(counterparty);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/counterparties/{counterparty.Id}",
            new { Id = counterparty.Id, counterparty.Name });
    }

    // PUT /api/counterparties/{id}
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateCounterpartyRequest request,
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

public sealed record CreateCounterpartyRequest(
    string Name,
    string Type,
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