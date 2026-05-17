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

        // Make Id value generated on add so EF knows the database will generate it
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        // Make Description required and constrain length
        builder.Property(t => t.Description)
            .IsRequired()
            .HasMaxLength(512);

        // Make Currency required and constrain length
        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(t => t.Amount)
            .HasPrecision(18, 2);

        // Ensure Date is stored as timestamptz in Postgres
        builder.Property(t => t.Date)
            .HasColumnType("timestamp with time zone");

        // CreatedAtUtc should be generated on add with a Postgres timezone default
        builder.Property(t => t.CreatedAtUtc)
            .ValueGeneratedOnAdd()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(t => t.Date);

        // These entity properties exist for application-level metadata but may not
        // be present in the database schema for older deployments. Ignore them
        // so EF Core does not attempt to query non-existent columns.
        builder.Ignore(t => t.CouponNumber);
        builder.Ignore(t => t.RawLine);
        builder.Ignore(t => t.SourceFile);
    }
}
