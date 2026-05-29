using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    internal sealed class ReconciledExpenseItemConfiguration
        : IEntityTypeConfiguration<ReconciledExpenseItem>
    {
        public void Configure(EntityTypeBuilder<ReconciledExpenseItem> builder)
        {
            builder.ToTable("ReconciledExpenseItems");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).ValueGeneratedOnAdd();

            // ── FK a cabecera ─────────────────────────────────────────
            builder.Property(i => i.ReconciledExpenseId)
                .IsRequired();

            // La relación inversa y el DeleteBehavior.Restrict
            // ya están configurados en ReconciledExpenseConfiguration.

            // ── Referencia al registro original ──────────────────────
            builder.Property(i => i.SourceEntityType)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(i => i.SourceId)
                .IsRequired()
                .HasColumnType("uuid");

            builder.Property(i => i.Role)
                .IsRequired()
                .HasConversion<int>();

            // ── Snapshot ──────────────────────────────────────────────
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

            builder.Property(i => i.ContributionScore)
                .HasColumnType("double precision");

            // ── Índices ───────────────────────────────────────────────

            // FK index (EF lo crea automático, pero lo hacemos explícito
            // para poder nombrarlo y usarlo en EXPLAIN ANALYZE)
            builder.HasIndex(i => i.ReconciledExpenseId)
                .HasDatabaseName("IX_ReconciledExpenseItems_ReconciledExpenseId");

            // Query crítica de UI: "¿este movimiento ya fue reconciliado?"
            // Permite mostrar badge/estado en listas de Transactions, ManualExpenses, BankStatements
            builder.HasIndex(i => new { i.SourceEntityType, i.SourceId })
                .HasDatabaseName("IX_ReconciledExpenseItems_Source");

            // Unique constraint: el mismo movimiento no puede aparecer
            // dos veces en la misma reconciliación
            builder.HasIndex(i => new { i.ReconciledExpenseId, i.SourceEntityType, i.SourceId })
                .IsUnique()
                .HasDatabaseName("UX_ReconciledExpenseItems_UniqueSourcePerExpense");
        }
    }
}
