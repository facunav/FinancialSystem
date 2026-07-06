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
    public DbSet<LegacyImportedExpense> LegacyImportedExpenses => Set<LegacyImportedExpense>();
    public DbSet<CounterParty> CounterParties => Set<CounterParty>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ClassifiedMovementConfiguration());
        modelBuilder.ApplyConfiguration(new ClassifiedMovementItemConfiguration());
        modelBuilder.ApplyConfiguration(new LegacyImportedExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new CounterPartyConfiguration());
    }
}
