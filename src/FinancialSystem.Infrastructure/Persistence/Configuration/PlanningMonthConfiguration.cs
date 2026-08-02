using FinancialSystem.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class PlanningMonthConfiguration : IEntityTypeConfiguration<PlanningMonth>
{
    public void Configure(EntityTypeBuilder<PlanningMonth> builder)
    {
        builder.ToTable("PlanningMonths");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Period)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.ExpectedIncome)
            .HasPrecision(18, 2);

        builder.HasIndex(e => e.Period)
            .IsUnique()
            .HasDatabaseName("IX_PlanningMonths_Period");
    }
}
