using FinancialSystem.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class PlanningItemConfiguration : IEntityTypeConfiguration<PlanningItem>
{
    public void Configure(EntityTypeBuilder<PlanningItem> builder)
    {
        builder.ToTable("PlanningItems");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.PlanningMonthId)
            .IsRequired();

        builder.HasOne(e => e.PlanningMonth)
            .WithMany(e => e.Items)
            .HasForeignKey(e => e.PlanningMonthId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ExpectedAmount)
            .HasPrecision(18, 2);

        builder.Property(e => e.DueDate)
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.IsPaid)
            .IsRequired();

        builder.Property(e => e.PaidAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(e => e.PlanningMonthId)
            .HasDatabaseName("IX_PlanningItems_PlanningMonthId");
    }
}
