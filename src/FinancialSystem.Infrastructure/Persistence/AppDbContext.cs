using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ManualExpense> ManualExpenses => Set<ManualExpense>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new ManualExpenseConfiguration());
    }
}
