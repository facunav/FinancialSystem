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
    DbSet<Counterparty> Counterparties { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<ImportBatchLine> ImportBatchLines { get; }
    DbSet<FinancialAccount> FinancialAccounts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
