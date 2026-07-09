using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Accounts;

internal sealed class FinancialAccountQueryService : IFinancialAccountQueryService
{
    private readonly IApplicationDbContext _db;

    public FinancialAccountQueryService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FinancialAccountSummary>> GetAllAsync(
        bool includeDeactivated = false, string? search = null, CancellationToken ct = default)
    {
        var query = _db.FinancialAccounts.AsNoTracking();

        if (!includeDeactivated)
            query = query.Where(a => !a.IsDeactivated);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => a.Name.ToLower().Contains(search.ToLower()));

        var accounts = await query
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        return accounts.Select(ToSummary).ToList().AsReadOnly();
    }

    public async Task<FinancialAccountSummary?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _db.FinancialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        return account is null ? null : ToSummary(account);
    }

    private static FinancialAccountSummary ToSummary(FinancialAccount a) => new(
        a.Id,
        a.Name,
        a.Type,
        a.AccountNumber,
        a.Currency,
        a.Notes,
        a.IsDeactivated,
        a.CreatedAt,
        a.UpdatedAt);
}
