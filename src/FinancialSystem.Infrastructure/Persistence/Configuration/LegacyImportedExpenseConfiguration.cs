using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    public sealed class LegacyImportedExpenseConfiguration
    : IEntityTypeConfiguration<LegacyImportedExpense>
    {
        public void Configure(EntityTypeBuilder<LegacyImportedExpense> builder)
        {
            builder.ToTable("LegacyImportedExpenses");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            // --------------------------------------------------------------------
            // Datos
            // --------------------------------------------------------------------

            builder.Property(x => x.Date)
                .IsRequired();

            builder.Property(x => x.Description)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.PaymentMethod)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.Currency)
                .HasColumnType("char(3)")
                .IsRequired();

            builder.Property(x => x.Notes)
                .HasMaxLength(1000);

            // --------------------------------------------------------------------
            // Legacy
            // --------------------------------------------------------------------

            builder.Property(x => x.Sheet)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.MonthLabel)
                .HasMaxLength(30);

            builder.Property(x => x.PaymentStatus)
                .HasMaxLength(50);

            builder.Property(x => x.PaidAt);

            // --------------------------------------------------------------------
            // Estado
            // --------------------------------------------------------------------

            builder.Property(x => x.IsDiscarded)
                .IsRequired();

            builder.Property(x => x.DiscardedAt);

            // --------------------------------------------------------------------
            // Importación
            // --------------------------------------------------------------------

            builder.Property(x => x.ExternalId)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(x => x.SourceFile)
                .HasMaxLength(255);

            builder.Property(x => x.SheetName)
                .HasMaxLength(100);

            builder.Property(x => x.ImportedAtUtc)
                .IsRequired();

            // --------------------------------------------------------------------
            // Índices
            // --------------------------------------------------------------------

            builder.HasIndex(x => x.ExternalId)
                .IsUnique();

            builder.HasIndex(x => x.IsDiscarded);

            builder.HasIndex(x => x.Date);

            builder.HasIndex(x => x.Amount);

            builder.HasIndex(x => new
            {
                x.IsDiscarded,
                x.Date
            });

            builder.HasIndex(x => new
            {
                x.IsDiscarded,
                x.Amount
            });

            builder.HasIndex(x => new
            {
                x.Date,
                x.Amount
            });
        }
    }
}
