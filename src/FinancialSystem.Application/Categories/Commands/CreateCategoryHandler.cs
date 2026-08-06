using System.Globalization;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Categories.Commands;

/// <summary>
/// Migrado desde CategoryEndpoints.Create (PATCH-046) -- misma lógica exacta: Name se
/// deriva de DisplayName normalizado (sin acentos, sin espacios) cuando no se provee
/// explícitamente; SortOrder nuevo es el máximo existente + 10; toda Category creada
/// acá es de usuario (IsSystem=false) y activa. La validación de forma del request
/// (FluentValidation) sigue viviendo en el Endpoint -- acá solo la regla de negocio
/// (unicidad de Name, derivación, cálculo de SortOrder).
/// </summary>
public sealed class CreateCategoryHandler
{
    private readonly IApplicationDbContext _db;

    public CreateCategoryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CreateCategoryResult> Handle(
        CreateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(command.Name)
            ? NormalizeName(command.DisplayName)
            : command.Name.Trim();

        var exists = await _db.Categories.AnyAsync(c => c.Name == name, cancellationToken);
        if (exists)
            return CreateCategoryResult.NameConflict(name);

        var maxSort = await _db.Categories.AsNoTracking()
            .MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? 0;

        var category = new Category
        {
            Name = name,
            DisplayName = command.DisplayName.Trim(),
            SortOrder = maxSort + 10,
            IsSystem = false,
            IsDeactivated = false,
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        return CreateCategoryResult.Success(
            new CategorySummary(category.Id, category.Name, category.DisplayName, category.SortOrder, category.IsDeactivated));
    }

    private static string NormalizeName(string displayName) =>
        new string(displayName.Trim()
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Replace(" ", "");
}
