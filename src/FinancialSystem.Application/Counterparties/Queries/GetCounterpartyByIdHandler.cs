using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Counterparties.Queries;

/// <summary>Migrado desde CounterpartyEndpoints.GetById (PATCH-047). Devuelve null si no existe -- el Endpoint traduce eso a 404.</summary>
public sealed class GetCounterpartyByIdHandler
{
    private readonly IApplicationDbContext _db;

    public GetCounterpartyByIdHandler(IApplicationDbContext db) => _db = db;

    public async Task<CounterpartySummary?> Handle(
        GetCounterpartyByIdQuery query, CancellationToken cancellationToken = default)
    {
        var c = await _db.Counterparties
            .AsNoTracking()
            .Include(x => x.DefaultCategory)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);

        return c is null ? null : CounterpartyMapping.ToSummary(c);
    }
}
