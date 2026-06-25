using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    internal sealed class ProcessedExpenseConfiguration : IEntityTypeConfiguration<ProcessedExpense>
    {
        public void Configure(EntityTypeBuilder<ProcessedExpense> builder)
        {
            builder.ToTable("ProcessedExpenses");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id).ValueGeneratedOnAdd();

            // ── Datos financieros canónicos ──────────────────────────────────────
            builder.Property(e => e.EffectiveDate)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.TotalAmount)
                .IsRequired()
                .HasPrecision(18, 2);

            builder.Property(e => e.Currency)
                .IsRequired()
                .HasMaxLength(3)
                .HasDefaultValue("ARS");

            builder.Property(e => e.Description)
                .IsRequired()
                .HasMaxLength(512);

            // ── Clasificación financiera ─────────────────────────────────────────
            builder.Property(e => e.CategoryId)
                .IsRequired();

            builder.HasOne(e => e.Category)
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(e => e.FinancialImpact)
                .IsRequired()
                .HasConversion<int>();

            // ── Estado ───────────────────────────────────────────────────────────
            builder.Property(e => e.Status)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(e => e.ProcessingSource)
                .IsRequired()
                .HasConversion<int>();

            // ── Review manual ────────────────────────────────────────────────────
            builder.Property(e => e.ReviewReason)
                .HasConversion<int>();

            builder.Property(e => e.ReviewNotes)
                .HasMaxLength(1024);

            // ── Métricas del motor (nullable) ────────────────────────────────────
            builder.Property(e => e.MatchScore)
                .HasColumnType("double precision");

            builder.Property(e => e.AmountDelta)
                .HasPrecision(18, 2);

            // ── Auditoría ────────────────────────────────────────────────────────
            builder.Property(e => e.CreatedAt)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.ProcessedAt)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.ProcessedBy)
                .HasMaxLength(256);

            // ── Relación con Items ───────────────────────────────────────────────
            builder.HasMany(e => e.Items)
                .WithOne(i => i.ProcessedExpense)
                .HasForeignKey(i => i.ProcessedExpenseId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Índices ──────────────────────────────────────────────────────────
            // Query MCP más frecuente: gastos por período
            builder.HasIndex(e => e.EffectiveDate)
                .HasDatabaseName("IX_ProcessedExpenses_EffectiveDate");

            // Query MCP: gastos por categoría
            builder.HasIndex(e => e.CategoryId)
                .HasDatabaseName("IX_ProcessedExpenses_CategoryId");

            // Query MCP: gastos reales (excluye transferencias internas)
            builder.HasIndex(e => e.FinancialImpact)
                .HasDatabaseName("IX_ProcessedExpenses_FinancialImpact");

            // Query UI: vista de estado
            builder.HasIndex(e => e.Status)
                .HasDatabaseName("IX_ProcessedExpenses_Status");

            // Query MCP compuesta: gastos reales por mes y categoría
            builder.HasIndex(e => new { e.EffectiveDate, e.CategoryId, e.FinancialImpact })
                .HasDatabaseName("IX_ProcessedExpenses_Date_Category_Impact");

            // Auditoría: quién procesó
            builder.HasIndex(e => new { e.ProcessedBy, e.ProcessedAt })
                .HasDatabaseName("IX_ProcessedExpenses_ProcessedBy_At")
                .HasFilter("\"ProcessedBy\" IS NOT NULL");
        }
    }
}
