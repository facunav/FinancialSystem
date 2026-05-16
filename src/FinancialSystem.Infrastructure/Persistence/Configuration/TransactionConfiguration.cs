using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Description).HasMaxLength(512);
        builder.Property(t => t.Currency).HasMaxLength(3);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.HasIndex(t => t.Date);
    }
}
