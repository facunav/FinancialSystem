using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Categories.Commands;
using FinancialSystem.Application.Categories.Queries;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Migrado al patrón Command/Query + Handler en PATCH-046 (ver
/// docs/Decisions/ADR-008-command-handler-sin-mediatr.md) -- ya no tiene lógica de
/// negocio propia: cada acción valida la forma del request (FluentValidation, sigue
/// siendo responsabilidad del endpoint -- validación HTTP, no regla de negocio),
/// arma el Command/Query correspondiente, lo pasa al Handler de
/// FinancialSystem.Application.Categories, y traduce el resultado a una respuesta
/// HTTP. Ninguna decisión (derivación de Name, unicidad, cálculo de SortOrder) vive
/// acá -- ver los Handlers de Application/Categories para esas reglas.
/// </summary>
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
        [FromServices] GetCategoriesHandler handler = null!,
        CancellationToken ct = default)
    {
        var categories = await handler.Handle(new GetCategoriesQuery(includeDeactivated), ct);

        return Results.Ok(categories
            .Select(c => new CategoryDto(c.Id, c.Name, c.DisplayName, c.SortOrder, c.IsDeactivated))
            .ToList());
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
        [FromServices] CreateCategoryHandler handler,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.BadRequest(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        var result = await handler.Handle(new CreateCategoryCommand(request.DisplayName, request.Name), ct);

        if (!result.IsSuccess)
            return Results.Conflict($"Ya existe una categoría con Name='{result.ConflictingName}'");

        var category = result.Category!;
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
        [FromServices] UpdateCategoryHandler handler,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Results.BadRequest(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        var result = await handler.Handle(new UpdateCategoryCommand(id, request.DisplayName, request.SortOrder), ct);
        if (!result.IsSuccess) return Results.NotFound();

        var category = result.Category!;
        return Results.Ok(new CategoryDto(category.Id, category.Name,
            category.DisplayName, category.SortOrder, category.IsDeactivated));
    }

    // DELETE /api/categories/{id} — desactiva, no elimina
    private static async Task<IResult> Deactivate(
        Guid id,
        [FromServices] DeactivateCategoryHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DeactivateCategoryCommand(id), ct);
        if (!result.IsSuccess) return Results.NotFound();

        return Results.Ok(new { Message = result.Message });
    }
}

public sealed record CreateCategoryRequest(string DisplayName, string? Name);
public sealed record UpdateCategoryRequest(string? DisplayName, int? SortOrder);
