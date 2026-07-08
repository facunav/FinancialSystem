using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class ImportBatchLineConfiguration : IEntityTypeConfiguration<ImportBatchLine>
{
    public void Configure(EntityTypeBuilder<ImportBatchLine> builder)
    {
        builder.ToTable("ImportBatchLines");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.ImportBatchId)
            .IsRequired();

        builder.HasOne(e => e.ImportBatch)
            .WithMany(e => e.Lines)
            .HasForeignKey(e => e.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.LineNumber)
            .IsRequired();

        builder.Property(e => e.RawText)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.Reason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.HasIndex(e => e.ImportBatchId)
            .HasDatabaseName("IX_ImportBatchLines_ImportBatchId");
    }
}
