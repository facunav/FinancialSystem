using FinancialSystem.Application.Abstractions;

namespace FinancialSystem.Application.Categories.Commands;

/// <summary>
/// Migrado desde CategoryEndpoints.Deactivate (PATCH-046) -- misma lógica exacta:
/// desactivar una Category ya desactivada no falla ni la modifica de nuevo, devuelve
/// el mismo mensaje informativo de antes.
/// </summary>
public sealed class DeactivateCategoryHandler
{
    private readonly IApplicationDbContext _db;

    public DeactivateCategoryHandler(IApplicationDbContext db) => _db = db;

    public async Task<DeactivateCategoryResult> Handle(
        DeactivateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FindAsync([command.CategoryId], cancellationToken);
        if (category is null) return DeactivateCategoryResult.NotFound();

        if (category.IsDeactivated)
            return DeactivateCategoryResult.Success(DeactivateCategoryOutcome.AlreadyDeactivated, "Ya estaba desactivada");

        category.IsDeactivated = true;
        await _db.SaveChangesAsync(cancellationToken);

        return DeactivateCategoryResult.Success(
            DeactivateCategoryOutcome.Deactivated, $"Categoría '{category.DisplayName}' desactivada");
    }
}
