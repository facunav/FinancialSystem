using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Marca LegacyImportedExpense.IsDiscarded=true para los ids indicados. No elimina
/// físicamente el registro (ver contrato documentado en LegacyImportedExpense).
/// </summary>
public sealed class DiscardLegacyCandidatesHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DiscardLegacyCandidatesHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<LegacyCandidatesBulkResult> Handle(
        DiscardLegacyCandidatesCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Ids.Count == 0)
            return LegacyCandidatesBulkResult.Failure(LegacyCandidatesBulkFailureReason.EmptyIds);

        var distinctIds = command.Ids.Distinct().ToList();

        var expenses = await _db.LegacyImportedExpenses
            .Where(e => distinctIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        foreach (var expense in expenses)
        {
            expense.IsDiscarded = true;
            expense.DiscardedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var updatedIds = expenses.Select(e => e.Id).ToList();
        var notFoundIds = distinctIds.Except(updatedIds).ToList();

        return LegacyCandidatesBulkResult.Success(updatedIds, notFoundIds);
    }
}
