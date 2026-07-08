using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.SourceFile)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(e => e.ContentHash)
            .IsRequired()
            .HasMaxLength(64); // SHA256 hex = exactamente 64 chars

        builder.Property(e => e.HandlerName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.StartedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.CompletedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.InsertedCount).IsRequired();
        builder.Property(e => e.DuplicateCount).IsRequired();
        builder.Property(e => e.FailedCount).IsRequired();
        builder.Property(e => e.SkippedCount).IsRequired();

        // ── Índices de consulta frecuente ─────────────────────────
        builder.HasIndex(e => e.StartedAtUtc)
            .HasDatabaseName("IX_ImportBatches_StartedAtUtc");

        builder.HasIndex(e => e.HandlerName)
            .HasDatabaseName("IX_ImportBatches_HandlerName");

        builder.HasIndex(e => e.ContentHash)
            .HasDatabaseName("IX_ImportBatches_ContentHash");
    }
}
