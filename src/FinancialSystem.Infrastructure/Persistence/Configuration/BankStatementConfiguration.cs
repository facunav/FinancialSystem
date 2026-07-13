using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class BankStatementConfiguration : IEntityTypeConfiguration<BankStatement>
{
    public void Configure(EntityTypeBuilder<BankStatement> builder)
    {
        builder.ToTable("BankStatements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Date)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.Concept)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.Detail)
            .HasMaxLength(256);

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("ARS");

        builder.Property(e => e.Balance)
            .HasPrecision(18, 2);

        builder.Property(e => e.BankName)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.AccountNumber)
            .HasMaxLength(64);

        // ── Idempotencia: índice único sobre ExternalId ───────────
        builder.Property(e => e.ExternalId)
            .IsRequired()
            .HasMaxLength(64);  // SHA256 hex = exactamente 64 chars

        builder.HasIndex(e => e.ExternalId)
            .IsUnique()
            .HasDatabaseName("IX_BankStatements_ExternalId");

        builder.Property(e => e.SourceFile).HasMaxLength(1024);
        builder.Property(e => e.SheetName).HasMaxLength(128);

        builder.Property(e => e.ImportedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        // ── Índices de consulta frecuente ─────────────────────────
        builder.HasIndex(e => e.Date)
            .HasDatabaseName("IX_BankStatements_Date");

        builder.HasIndex(e => new { e.Date, e.BankName })
            .HasDatabaseName("IX_BankStatements_Date_Bank");

        // ── Cuenta financiera (opcional) ───────────────────────────
        builder.HasOne(e => e.FinancialAccount)
            .WithMany()
            .HasForeignKey(e => e.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.FinancialAccountId);

        // ── Enriquecimiento desde Tarjeta de Débito (opcional) ─────
        builder.Property(e => e.Merchant)
            .HasMaxLength(256);

        builder.Property(e => e.MerchantAtUtc)
            .HasColumnType("timestamp with time zone");
    }
}
