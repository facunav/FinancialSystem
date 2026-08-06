using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;

namespace FinancialSystem.Application.Transactions.Commands;

/// <summary>
/// Migrado desde TransactionEndpoints.AssignFinancialAccount (PATCH-049) -- misma
/// lógica exacta: si se informa un FinancialAccountId, debe existir; null desasigna
/// sin validar nada.
/// </summary>
public sealed class AssignTransactionFinancialAccountHandler
{
    private readonly IApplicationDbContext _db;

    public AssignTransactionFinancialAccountHandler(IApplicationDbContext db) => _db = db;

    public async Task<AssignTransactionFinancialAccountResult> Handle(
        AssignTransactionFinancialAccountCommand command, CancellationToken cancellationToken = default)
    {
        var transaction = await _db.Transactions.FindAsync([command.TransactionId], cancellationToken);
        if (transaction is null) return AssignTransactionFinancialAccountResult.NotFound();

        if (command.FinancialAccountId is { } accountId)
        {
            var exists = await FinancialAccountExistenceCheck.ExistsAsync(_db, accountId, cancellationToken);
            if (!exists) return AssignTransactionFinancialAccountResult.InvalidAccount(accountId);
        }

        transaction.FinancialAccountId = command.FinancialAccountId;
        await _db.SaveChangesAsync(cancellationToken);

        return AssignTransactionFinancialAccountResult.Success(transaction.Id, transaction.FinancialAccountId);
    }
}
