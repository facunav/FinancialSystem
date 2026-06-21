using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    internal sealed class ReconciledExpenseConfiguration
        : IEntityTypeConfiguration<ReconciledExpense>
    {
        public void Configure(EntityTypeBuilder<ReconciledExpense> builder)
        {
            builder.ToTable("ReconciledExpenses");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id).ValueGeneratedOnAdd();

            // ── Período ───────────────────────────────────────────────
            // DateOnly → date (sin hora) en PostgreSQL.
            // EF Core + Npgsql 9 soporta DateOnly nativo con columna 'date'.
            builder.Property(e => e.PeriodStart)
                .IsRequired()
                .HasColumnType("date");

            builder.Property(e => e.PeriodEnd)
                .IsRequired()
                .HasColumnType("date");

            // ── Datos consolidados ────────────────────────────────────
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

            // ── Estado ────────────────────────────────────────────────
            // Almacenado como int para evitar dependencia de strings en queries.
            // Los valores son estables y documentados en el enum.
            builder.Property(e => e.Status)
                .IsRequired()
                .HasConversion<int>();

            // MatchConfidence como varchar: "High" | "Medium" | "Low".
            // No se usa enum para flexibilidad futura sin migration.
            builder.Property(e => e.MatchConfidence)
                .IsRequired()
                .HasMaxLength(16);

            builder.Property(e => e.MatchScore)
                .IsRequired()
                .HasColumnType("double precision");

            // ── Auditoría ─────────────────────────────────────────────
            builder.Property(e => e.CreatedAt)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp with time zone");

            builder.Property(e => e.ConfirmedBy)
                .HasMaxLength(256);

            builder.Property(x => x.ReviewReason)
                .HasConversion<int>();

            builder.Property(x => x.ReviewNotes)
                .HasMaxLength(1000);

            // ── Relación con Items ────────────────────────────────────
            // NO cascade delete. Borrado controlado explícitamente.
            // La aplicación debe eliminar los Items antes de eliminar el padre.
            builder.HasMany(e => e.Items)
                .WithOne(i => i.ReconciledExpense)
                .HasForeignKey(i => i.ReconciledExpenseId)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Índices ───────────────────────────────────────────────

            // Consultas frecuentes de UI: "dame las reconciliaciones del mes X"
            builder.HasIndex(e => new { e.PeriodStart, e.PeriodEnd })
                .HasDatabaseName("IX_ReconciledExpenses_Period");

            // Consultas frecuentes de UI: "dame las pendientes de confirmar"
            builder.HasIndex(e => e.Status)
                .HasDatabaseName("IX_ReconciledExpenses_Status");

            // Consultas combinadas: "pendientes de este mes" (query más común en dashboard)
            builder.HasIndex(e => new { e.Status, e.PeriodStart })
                .HasDatabaseName("IX_ReconciledExpenses_Status_PeriodStart");

            // Búsquedas por fecha efectiva para vistas de línea de tiempo
            builder.HasIndex(e => e.EffectiveDate)
                .HasDatabaseName("IX_ReconciledExpenses_EffectiveDate");

            // Auditoría: "qué confirmó el usuario X en el período Y"
            builder.HasIndex(e => new { e.ConfirmedBy, e.ConfirmedAt })
                .HasDatabaseName("IX_ReconciledExpenses_ConfirmedBy_ConfirmedAt")
                .HasFilter("\"ConfirmedBy\" IS NOT NULL");   // índice parcial: solo filas confirmadas

            builder.Property(e => e.AmountDelta)
                .IsRequired()
                .HasPrecision(18, 2)
                .HasDefaultValue(0m);

            builder.Property(e => e.HasAmountMismatch)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(e => e.GroupingMode)
                .IsRequired()
                .HasConversion<int>()
                .HasDefaultValue(ReconciliationGroupingMode.EngineSuggested);
        }
    }
}
