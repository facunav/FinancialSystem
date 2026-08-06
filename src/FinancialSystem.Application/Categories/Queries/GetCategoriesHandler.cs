using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Categories.Queries;

/// <summary>
/// Migrado desde CategoryEndpoints.GetAll (PATCH-046) -- mismo criterio de antes:
/// excluye desactivadas salvo que se pida explícitamente lo contrario, mismo orden
/// (SortOrder, luego DisplayName). Sin escritura, sin efectos secundarios.
/// </summary>
public sealed class GetCategoriesHandler
{
    private readonly IApplicationDbContext _db;

    public GetCategoriesHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategorySummary>> Handle(
        GetCategoriesQuery query, CancellationToken cancellationToken = default)
    {
        var categories = _db.Categories.AsNoTracking();
        if (!query.IncludeDeactivated)
            categories = categories.Where(c => !c.IsDeactivated);

        return await categories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.DisplayName)
            .Select(c => new CategorySummary(c.Id, c.Name, c.DisplayName, c.SortOrder, c.IsDeactivated))
            .ToListAsync(cancellationToken);
    }
}
