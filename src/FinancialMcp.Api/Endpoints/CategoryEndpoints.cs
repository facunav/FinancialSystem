using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

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
