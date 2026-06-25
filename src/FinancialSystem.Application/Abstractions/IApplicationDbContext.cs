using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<ManualExpense> ManualExpenses { get; }
    DbSet<BankStatement> BankStatements { get; }
    DbSet<Category> Categories { get; }
    DbSet<ProcessedExpense> ProcessedExpenses { get; }
    DbSet<ProcessedExpenseItem> ProcessedExpenseItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
