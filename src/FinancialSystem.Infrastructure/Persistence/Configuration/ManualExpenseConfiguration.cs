using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class ManualExpenseConfiguration : IEntityTypeConfiguration<ManualExpense>
{
    public void Configure(EntityTypeBuilder<ManualExpense> builder)
    {
        builder.ToTable("ManualExpenses");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // ── Campos requeridos ────────────────────────────────────
        builder.Property(e => e.Date)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.PaymentMethod)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("ARS");

        builder.Property(e => e.Sheet)
            .IsRequired()
            .HasConversion<int>();

        // ── Campos opcionales ────────────────────────────────────
        builder.Property(e => e.Notes).HasMaxLength(1024);
        builder.Property(e => e.MonthLabel).HasMaxLength(32);
        builder.Property(e => e.PaymentStatus).HasMaxLength(32);
        builder.Property(e => e.PaidAt)
            .HasColumnType("timestamp with time zone");

        // ── Trazabilidad ─────────────────────────────────────────
        builder.Property(e => e.ExternalId)
            .IsRequired()
            .HasMaxLength(64);  // SHA256 hex = 64 chars

        // Índice único: garantiza idempotencia a nivel DB.
        // Re-importar el mismo Excel produce conflicto → ignoramos el insert.
        builder.HasIndex(e => e.ExternalId)
            .IsUnique()
            .HasDatabaseName("IX_ManualExpenses_ExternalId");

        builder.Property(e => e.SourceFile).HasMaxLength(1024);
        builder.Property(e => e.SheetName).HasMaxLength(128);

        builder.Property(e => e.ImportedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        // ── Índices de consulta ───────────────────────────────────
        builder.HasIndex(e => e.Date)
            .HasDatabaseName("IX_ManualExpenses_Date");

        builder.HasIndex(e => new { e.Date, e.Sheet })
            .HasDatabaseName("IX_ManualExpenses_Date_Sheet");
    }
}
