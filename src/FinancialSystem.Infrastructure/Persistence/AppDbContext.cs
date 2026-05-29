using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Reconciliation;
using FinancialSystem.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ManualExpense> ManualExpenses => Set<ManualExpense>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<ReconciledExpense> ReconciledExpenses => Set<ReconciledExpense>();
    public DbSet<ReconciledExpenseItem> ReconciledExpenseItems => Set<ReconciledExpenseItem>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new ManualExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciledExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciledExpenseItemConfiguration());
    }
}
