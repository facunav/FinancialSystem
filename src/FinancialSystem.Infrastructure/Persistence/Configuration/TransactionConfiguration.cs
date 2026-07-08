using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);

        // Make Id value generated on add so EF knows the database will generate it
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        // Make Description required and constrain length
        builder.Property(t => t.Description)
            .IsRequired()
            .HasMaxLength(512);

        // Make Currency required and constrain length
        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(t => t.Amount)
            .HasPrecision(18, 2);

        // Ensure Date is stored as timestamptz in Postgres
        builder.Property(t => t.Date)
            .HasColumnType("timestamp with time zone");

        // CreatedAtUtc should be generated on add with a Postgres timezone default
        builder.Property(t => t.CreatedAtUtc)
            .ValueGeneratedOnAdd()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(t => t.Date);

        // ── Idempotencia: índice único sobre ExternalId ───────────
        // Nullable a nivel de columna: filas existentes antes de esta migración quedan sin
        // valor. Postgres no considera NULL == NULL en un índice único, así que no chocan
        // entre sí ni bloquean la migración en una tabla ya poblada. Toda fila nueva la recibe
        // (ver ImportFileProcessingSink), y esas sí quedan protegidas por la unicidad.
        builder.Property(t => t.ExternalId)
            .HasMaxLength(64); // SHA256 hex = exactamente 64 chars

        builder.HasIndex(t => t.ExternalId)
            .IsUnique()
            .HasDatabaseName("IX_Transactions_ExternalId");
    }
}
