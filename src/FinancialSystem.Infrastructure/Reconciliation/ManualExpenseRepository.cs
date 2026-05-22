using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Reconciliation;

internal sealed class ManualExpenseRepository : IManualExpenseRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ManualExpenseRepository> _logger;

    public ManualExpenseRepository(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<ManualExpenseRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ManualExpense>> GetByPeriodAsync(
        DateOnly from,
        DateOnly to,
        ManualExpenseSheet? sheet = null,
        CancellationToken ct = default)
    {
        // Convertir DateOnly a DateTime UTC para comparar contra la columna Date
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.ManualExpenses
            .AsNoTracking()
            .Where(e => e.Date >= fromUtc && e.Date <= toUtc);

        if (sheet.HasValue)
            query = query.Where(e => e.Sheet == sheet.Value);

        var results = await query
            .OrderBy(e => e.Date)
            .ToListAsync(ct);

        _logger.LogDebug(
            "ManualExpenseRepository: {Count} gastos entre {From} y {To} (sheet={Sheet})",
            results.Count, from, to, sheet?.ToString() ?? "All");

        return results.AsReadOnly();
    }
}
