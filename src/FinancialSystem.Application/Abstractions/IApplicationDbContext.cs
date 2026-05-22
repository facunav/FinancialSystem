using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Transaction> Transactions { get; }
    DbSet<ManualExpense> ManualExpenses { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
