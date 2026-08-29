using FinancialSystem.Domain.Dedupe;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class MovementIdentityLinkRollbackConfiguration : IEntityTypeConfiguration<MovementIdentityLinkRollback>
{
    public void Configure(EntityTypeBuilder<MovementIdentityLinkRollback> builder)
    {
        builder.ToTable("MovementIdentityLinkRollbacks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdentityGroupId).IsRequired();

        builder.Property(x => x.RolledBackBy).IsRequired().HasMaxLength(128);
        builder.Property(x => x.RolledBackAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(2048);

        // Índice único -- backstop real de idempotencia/concurrencia (ver doc-comment de
        // la entidad y MovementIdentityLinkRollbackService.RollbackAsync): un segundo
        // intento de revertir el mismo IdentityGroupId choca acá, nunca duplica la
        // auditoría. Mismo mecanismo que ya usa MovementIdentityLink con
        // (SourceEntityType, SourceId).
        builder.HasIndex(x => x.IdentityGroupId)
            .IsUnique()
            .HasDatabaseName("IX_MovementIdentityLinkRollbacks_IdentityGroupId_Unique");
    }
}
