using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Planning.Commands;

public sealed class UpdateExpectedIncomeHandler
{
    private readonly IApplicationDbContext _db;

    public UpdateExpectedIncomeHandler(IApplicationDbContext db) => _db = db;

    public async Task<UpdateExpectedIncomeResult> Handle(
        UpdateExpectedIncomeCommand command, CancellationToken cancellationToken = default)
    {
        var month = await _db.PlanningMonths
            .FirstOrDefaultAsync(m => m.Id == command.PlanningMonthId, cancellationToken);

        if (month is null)
            return UpdateExpectedIncomeResult.NotFound();

        month.ExpectedIncome = command.ExpectedIncome;

        await _db.SaveChangesAsync(cancellationToken);

        return UpdateExpectedIncomeResult.Success();
    }
}
