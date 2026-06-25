using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    internal sealed class ProcessedExpenseItemConfiguration : IEntityTypeConfiguration<ProcessedExpenseItem>
    {
        public void Configure(EntityTypeBuilder<ProcessedExpenseItem> builder)
        {
            builder.ToTable("ProcessedExpenseItems");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).ValueGeneratedOnAdd();

            // ── FK a cabecera ────────────────────────────────────────────────────
            // La relación y DeleteBehavior.Restrict están configurados en ProcessedExpenseConfiguration.
            builder.Property(i => i.ProcessedExpenseId).IsRequired();

            // ── Referencia al registro original ─────────────────────────────────
            builder.Property(i => i.SourceEntityType)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(i => i.SourceId)
                .IsRequired()
                .HasColumnType("uuid");

            builder.Property(i => i.Role)
                .IsRequired()
                .HasConversion<int>();

            // ── Snapshot inmutable ───────────────────────────────────────────────
            builder.Property(i => i.OriginalAmount)
                .IsRequired()
                .HasPrecision(18, 2);

            builder.Property(i => i.OriginalDate)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(i => i.OriginalDescription)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(i => i.OriginalCurrency)
                .IsRequired()
                .HasMaxLength(3)
                .HasDefaultValue("ARS");

            builder.Property(i => i.OriginalSourceFile)
                .HasMaxLength(1024);

            // ── Índices ──────────────────────────────────────────────────────────
            builder.HasIndex(i => i.ProcessedExpenseId)
                .HasDatabaseName("IX_ProcessedExpenseItems_ProcessedExpenseId");

            // Query crítica: "¿este movimiento ya fue procesado?"
            builder.HasIndex(i => new { i.SourceEntityType, i.SourceId })
                .HasDatabaseName("IX_ProcessedExpenseItems_Source");

            // Unique: el mismo movimiento no puede aparecer dos veces en el mismo expense
            builder.HasIndex(i => new { i.ProcessedExpenseId, i.SourceEntityType, i.SourceId })
                .IsUnique()
                .HasDatabaseName("UX_ProcessedExpenseItems_UniqueSourcePerExpense");
        }
    }

}
