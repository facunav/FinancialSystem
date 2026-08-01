using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class MovementAuditDecisionConfiguration : IEntityTypeConfiguration<MovementAuditDecision>
{
    public void Configure(EntityTypeBuilder<MovementAuditDecision> builder)
    {
        builder.ToTable("MovementAuditDecisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SourceEntityType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.SourceId)
            .IsRequired();

        builder.Property(x => x.ReviewedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.SourceEntityType, x.SourceId }).IsUnique();
    }
}
