using FinancialSystem.Domain.Dedupe;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class MovementIdentityLinkRollbackMemberConfiguration : IEntityTypeConfiguration<MovementIdentityLinkRollbackMember>
{
    public void Configure(EntityTypeBuilder<MovementIdentityLinkRollbackMember> builder)
    {
        builder.ToTable("MovementIdentityLinkRollbackMembers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.RollbackId).IsRequired();

        // FK real hacia MovementIdentityLinkRollback -- a diferencia de MovementIdentityLink
        // (referencia blanda hacia BankStatement), esta relación es interna al propio
        // subsistema de auditoría de Dedupe, mismo criterio que
        // InvestigationFinding->Investigation. Cascade: si alguna vez se borrara un
        // registro de rollback, sus miembros no deben quedar huérfanos -- aunque hoy no
        // existe ningún camino de código que borre un MovementIdentityLinkRollback.
        builder.HasOne(x => x.Rollback)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.RollbackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.SourceEntityType).HasConversion<int>().IsRequired();
        builder.Property(x => x.SourceId).IsRequired();

        builder.Property(x => x.Role).HasConversion<int>().IsRequired();
        builder.Property(x => x.Classification).HasConversion<int>().IsRequired();

        builder.Property(x => x.Evidence).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.OriginalCreatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(x => x.OriginalCreatedBy).IsRequired().HasMaxLength(128);

        builder.HasIndex(x => x.RollbackId)
            .HasDatabaseName("IX_MovementIdentityLinkRollbackMembers_RollbackId");
    }
}
