using FinancialSystem.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class InvestigationReferenceConfiguration : IEntityTypeConfiguration<InvestigationReference>
{
    public void Configure(EntityTypeBuilder<InvestigationReference> builder)
    {
        builder.ToTable("InvestigationReferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.InvestigationId)
            .IsRequired();

        builder.HasOne(x => x.Investigation)
            .WithMany(x => x.References)
            .HasForeignKey(x => x.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.SourceEntityType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.SourceId)
            .IsRequired();

        builder.HasIndex(x => x.InvestigationId);
        builder.HasIndex(x => new { x.SourceEntityType, x.SourceId });
    }
}
