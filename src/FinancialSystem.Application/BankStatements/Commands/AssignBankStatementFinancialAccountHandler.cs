using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;

namespace FinancialSystem.Application.BankStatements.Commands;

/// <summary>
/// Migrado desde BankStatementEndpoints.AssignFinancialAccount (PATCH-049) -- misma
/// lógica exacta: si se informa un FinancialAccountId, debe existir; null desasigna
/// sin validar nada. No interfiere con la autoasignación que hace
/// BbvaBankStatementImporter durante la importación (Infrastructure, sin tocar) --
/// esta es la asignación/corrección manual vía API, un camino totalmente separado.
/// </summary>
public sealed class AssignBankStatementFinancialAccountHandler
{
    private readonly IApplicationDbContext _db;

    public AssignBankStatementFinancialAccountHandler(IApplicationDbContext db) => _db = db;

    public async Task<AssignBankStatementFinancialAccountResult> Handle(
        AssignBankStatementFinancialAccountCommand command, CancellationToken cancellationToken = default)
    {
        var bankStatement = await _db.BankStatements.FindAsync([command.BankStatementId], cancellationToken);
        if (bankStatement is null) return AssignBankStatementFinancialAccountResult.NotFound();

        if (command.FinancialAccountId is { } accountId)
        {
            var exists = await FinancialAccountExistenceCheck.ExistsAsync(_db, accountId, cancellationToken);
            if (!exists) return AssignBankStatementFinancialAccountResult.InvalidAccount(accountId);
        }

        bankStatement.FinancialAccountId = command.FinancialAccountId;
        await _db.SaveChangesAsync(cancellationToken);

        return AssignBankStatementFinancialAccountResult.Success(bankStatement.Id, bankStatement.FinancialAccountId);
    }
}
