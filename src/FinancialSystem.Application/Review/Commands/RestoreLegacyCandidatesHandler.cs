using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>Marca LegacyImportedExpense.IsDiscarded=false para los ids indicados.</summary>
public sealed class RestoreLegacyCandidatesHandler
{
    private readonly IApplicationDbContext _db;

    public RestoreLegacyCandidatesHandler(IApplicationDbContext db) => _db = db;

    public async Task<LegacyCandidatesBulkResult> Handle(
        RestoreLegacyCandidatesCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Ids.Count == 0)
            return LegacyCandidatesBulkResult.Failure(LegacyCandidatesBulkFailureReason.EmptyIds);

        var distinctIds = command.Ids.Distinct().ToList();

        var expenses = await _db.LegacyImportedExpenses
            .Where(e => distinctIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        foreach (var expense in expenses)
        {
            expense.IsDiscarded = false;
            expense.DiscardedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var updatedIds = expenses.Select(e => e.Id).ToList();
        var notFoundIds = distinctIds.Except(updatedIds).ToList();

        return LegacyCandidatesBulkResult.Success(updatedIds, notFoundIds);
    }
}
