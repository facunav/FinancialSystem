using FinancialSystem.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class InvestigationFindingConfiguration : IEntityTypeConfiguration<InvestigationFinding>
{
    public void Configure(EntityTypeBuilder<InvestigationFinding> builder)
    {
        builder.ToTable("InvestigationFindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.InvestigationId)
            .IsRequired();

        builder.HasOne(x => x.Investigation)
            .WithMany(x => x.Findings)
            .HasForeignKey(x => x.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Text)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.InvestigationId);
    }
}
