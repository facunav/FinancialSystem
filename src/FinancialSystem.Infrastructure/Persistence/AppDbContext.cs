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
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProcessedExpense> ProcessedExpenses => Set<ProcessedExpense>();
    public DbSet<ProcessedExpenseItem> ProcessedExpenseItems => Set<ProcessedExpenseItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new ManualExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new BankStatementConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedExpenseConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedExpenseItemConfiguration());
    }
}
