using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    public sealed class ClassifiedMovementItemConfiguration
    : IEntityTypeConfiguration<ClassifiedMovementItem>
    {
        public void Configure(EntityTypeBuilder<ClassifiedMovementItem> builder)
        {
            builder.ToTable("ClassifiedMovementItems");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            // --------------------------------------------------------------------
            // Relación
            // --------------------------------------------------------------------

            builder.Property(x => x.ClassifiedMovementId)
                .IsRequired();

            builder.HasOne(x => x.ClassifiedMovement)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ClassifiedMovementId)
                .OnDelete(DeleteBehavior.Cascade);

            // --------------------------------------------------------------------
            // Referencia al origen
            // --------------------------------------------------------------------

            builder.Property(x => x.SourceEntityType)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.SourceId)
                .IsRequired();

            builder.Property(x => x.Role)
                .HasConversion<int>()
                .IsRequired();

            // --------------------------------------------------------------------
            // Snapshot
            // --------------------------------------------------------------------

            builder.Property(x => x.OriginalAmount)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.OriginalDate)
                .IsRequired();

            builder.Property(x => x.OriginalDescription)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.OriginalCurrency)
                .HasColumnType("char(3)")
                .IsRequired();

            builder.Property(x => x.OriginalSourceFile)
                .HasMaxLength(255);

            // --------------------------------------------------------------------
            // Índices
            // --------------------------------------------------------------------

            builder.HasIndex(x => x.ClassifiedMovementId);

            builder.HasIndex(x => new
            {
                x.SourceEntityType,
                x.SourceId
            });

            builder.HasIndex(x => new
            {
                x.ClassifiedMovementId,
                x.Role
            });
        }
    }
}
