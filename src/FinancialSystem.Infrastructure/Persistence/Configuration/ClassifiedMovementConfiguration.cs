using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    public sealed class ClassifiedMovementConfiguration : IEntityTypeConfiguration<ClassifiedMovement>
    {
        public void Configure(EntityTypeBuilder<ClassifiedMovement> builder)
        {
            builder.ToTable("ClassifiedMovements");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            // --------------------------------------------------------------------
            // Datos financieros
            // --------------------------------------------------------------------

            builder.Property(x => x.EffectiveDate)
                .IsRequired();

            builder.Property(x => x.TotalAmount)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();

            builder.Property(x => x.Description)
                .HasMaxLength(500)
                .IsRequired();

            // --------------------------------------------------------------------
            // Clasificación
            // --------------------------------------------------------------------

            builder.Property(x => x.MovementType)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.FinancialImpact)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.ProcessingSource)
                .HasConversion<int>()
                .IsRequired();

            // --------------------------------------------------------------------
            // Comentarios y métricas
            // --------------------------------------------------------------------

            builder.Property(x => x.Comment)
                .HasMaxLength(1000);

            builder.Property(x => x.MatchScore);

            builder.Property(x => x.AmountDelta)
                .HasPrecision(18, 2);

            // --------------------------------------------------------------------
            // Auditoría
            // --------------------------------------------------------------------

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.ProcessedAt)
                .IsRequired();

            builder.Property(x => x.ProcessedBy)
                .HasMaxLength(100);

            // --------------------------------------------------------------------
            // Relaciones
            // --------------------------------------------------------------------

            builder.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Counterparty)
                .WithMany()
                .HasForeignKey(x => x.CounterpartyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.Items)
                .WithOne(x => x.ClassifiedMovement)
                .HasForeignKey(x => x.ClassifiedMovementId)
                .OnDelete(DeleteBehavior.Cascade);

            // --------------------------------------------------------------------
            // Índices
            // --------------------------------------------------------------------

            builder.HasIndex(x => x.EffectiveDate);

            builder.HasIndex(x => x.CategoryId);

            builder.HasIndex(x => x.CounterpartyId);

            builder.HasIndex(x => x.FinancialImpact);

            builder.HasIndex(x => x.MovementType);

            builder.HasIndex(x => new
            {
                x.EffectiveDate,
                x.CategoryId
            });

            builder.HasIndex(x => new
            {
                x.CounterpartyId,
                x.FinancialImpact
            });
        }
    }
}
