using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ClassifiedMovement> ClassifiedMovements => Set<ClassifiedMovement>();
    public DbSet<ClassifiedMovementItem> ClassifiedMovementItems => Set<ClassifiedMovementItem>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchLine> ImportBatchLines => Set<ImportBatchLine>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ClassifiedMovementConfiguration());
        modelBuilder.ApplyConfiguration(new ClassifiedMovementItemConfiguration());
        modelBuilder.ApplyConfiguration(new CounterpartyConfiguration());
        modelBuilder.ApplyConfiguration(new ImportBatchConfiguration());
        modelBuilder.ApplyConfiguration(new ImportBatchLineConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialAccountConfiguration());
    }
}
