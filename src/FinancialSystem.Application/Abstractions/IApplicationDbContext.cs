using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<BankStatement> BankStatements { get; }
    DbSet<Category> Categories { get; }
    DbSet<ClassifiedMovement> ClassifiedMovements { get; }
    DbSet<ClassifiedMovementItem> ClassifiedMovementItems { get; }
    DbSet<LegacyImportedExpense> LegacyImportedExpenses { get; }
    DbSet<Counterparty> Counterparties { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
