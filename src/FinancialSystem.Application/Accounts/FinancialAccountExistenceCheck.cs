using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Accounts;

/// <summary>
/// Chequeo de existencia de un FinancialAccount por Id -- compartido por los Handlers
/// de AssignTransactionFinancialAccount y AssignBankStatementFinancialAccount
/// (PATCH-049), antes duplicado idénticamente en TransactionEndpoints y
/// BankStatementEndpoints (misma consulta AnyAsync en ambos).
/// </summary>
public static class FinancialAccountExistenceCheck
{
    public static Task<bool> ExistsAsync(IApplicationDbContext db, Guid financialAccountId, CancellationToken ct) =>
        db.FinancialAccounts.AnyAsync(a => a.Id == financialAccountId, ct);
}
