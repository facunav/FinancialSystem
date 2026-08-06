using FinancialSystem.Application.Abstractions;

namespace FinancialSystem.Application.Categories.Commands;

/// <summary>
/// Migrado desde CategoryEndpoints.Update (PATCH-046) -- misma lógica exacta: solo
/// actualiza DisplayName cuando viene con contenido no vacío, solo actualiza
/// SortOrder cuando viene provisto. Name e IsSystem nunca se tocan acá.
/// </summary>
public sealed class UpdateCategoryHandler
{
    private readonly IApplicationDbContext _db;

    public UpdateCategoryHandler(IApplicationDbContext db) => _db = db;

    public async Task<UpdateCategoryResult> Handle(
        UpdateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FindAsync([command.CategoryId], cancellationToken);
        if (category is null) return UpdateCategoryResult.NotFound();

        if (!string.IsNullOrWhiteSpace(command.DisplayName))
            category.DisplayName = command.DisplayName.Trim();

        if (command.SortOrder.HasValue)
            category.SortOrder = command.SortOrder.Value;

        await _db.SaveChangesAsync(cancellationToken);

        return UpdateCategoryResult.Success(
            new CategorySummary(category.Id, category.Name, category.DisplayName, category.SortOrder, category.IsDeactivated));
    }
}
