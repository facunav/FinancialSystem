using FinancialSystem.Domain.Dedupe;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class MovementIdentityLinkConfiguration : IEntityTypeConfiguration<MovementIdentityLink>
{
    public void Configure(EntityTypeBuilder<MovementIdentityLink> builder)
    {
        builder.ToTable("MovementIdentityLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdentityGroupId).IsRequired();

        builder.Property(x => x.SourceEntityType).HasConversion<int>().IsRequired();
        builder.Property(x => x.SourceId).IsRequired();

        builder.Property(x => x.Role).HasConversion<int>().IsRequired();
        builder.Property(x => x.Classification).HasConversion<int>().IsRequired();

        builder.Property(x => x.Evidence).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(128);

        // Deliberadamente sin HasOne/HasForeignKey/WithMany/OnDelete hacia BankStatement --
        // referencia blanda, mismo criterio que ClassifiedMovementItem/InvestigationReference.
        // No agregar navegación ni FK acá.

        builder.HasIndex(x => x.IdentityGroupId)
            .HasDatabaseName("IX_MovementIdentityLinks_IdentityGroupId");

        // Garantía de cardinalidad 1->1 por fila física (ver doc-comment de la entidad):
        // una representación física no puede aparecer en más de un link, para siempre.
        builder.HasIndex(x => new { x.SourceEntityType, x.SourceId })
            .IsUnique()
            .HasDatabaseName("IX_MovementIdentityLinks_Source_Unique");
    }
}
