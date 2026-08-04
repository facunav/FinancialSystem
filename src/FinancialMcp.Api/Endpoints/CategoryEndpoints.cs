using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(
        this IEndpointRouteBuilder app)
    {
        // Patch 0060 (PATCH-011): protegido con la misma infraestructura del Patch 0058,
        // incluidas las lecturas -- ver el comentario equivalente en
        // FinancialAccountEndpoints para la justificación de la decisión.
        var group = app.MapGroup("/api/categories").WithTags("Categories").RequireAuthorization();

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
    //
    // Patch 0065 (PATCH-016): validación migrada a FluentValidation (ver
    // Validation/CreateCategoryRequestValidator.cs) -- mismo formato de respuesta que
    // antes (Results.BadRequest(string)), sin lanzar excepciones por errores de
    // validación.
    private static async Task<IResult> Create(
        [FromBody] CreateCategoryRequest request,
        [FromServices] IValidator<CreateCategoryRequest> validator,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.BadRequest(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

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
    //
    // Patch 0065 (PATCH-016): validación migrada a FluentValidation (ver
    // Validation/UpdateCategoryRequestValidator.cs) -- un DisplayName nulo/vacío sigue
    // sin ser un error acá (significa "no cambiar este campo"), solo se valida su
    // longitud máxima cuando se provee un valor.
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        [FromServices] IValidator<UpdateCategoryRequest> validator,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.BadRequest(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

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
