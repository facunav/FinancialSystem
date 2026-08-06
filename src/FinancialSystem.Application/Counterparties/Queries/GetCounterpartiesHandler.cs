using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Counterparties.Queries;

/// <summary>
/// Migrado desde CounterpartyEndpoints.GetAll (PATCH-047) -- misma lógica exacta:
/// AsNoTracking + Include(DefaultCategory), excluye desactivadas salvo que se pida lo
/// contrario, filtro opcional por substring de Name, orden por Name.
/// </summary>
public sealed class GetCounterpartiesHandler
{
    private readonly IApplicationDbContext _db;

    public GetCounterpartiesHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<CounterpartySummary>> Handle(
        GetCounterpartiesQuery query, CancellationToken cancellationToken = default)
    {
        var q = _db.Counterparties
            .AsNoTracking()
            .Include(c => c.DefaultCategory);

        var filtered = query.IncludeDeactivated
            ? q.AsQueryable()
            : q.Where(c => !c.IsDeactivated);

        if (!string.IsNullOrWhiteSpace(query.Search))
            filtered = filtered.Where(c => c.Name.ToLower().Contains(query.Search.ToLower()));

        return await filtered
            .OrderBy(c => c.Name)
            .Select(c => CounterpartyMapping.ToSummary(c))
            .ToListAsync(cancellationToken);
    }
}
