using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    public sealed class CounterPartyConfiguration : IEntityTypeConfiguration<CounterParty>
    {
        public void Configure(EntityTypeBuilder<CounterParty> builder)
        {
            builder.ToTable("CounterParties");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            // --------------------------------------------------------------------
            // Datos
            // --------------------------------------------------------------------

            builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.Type)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.Notes)
                .HasMaxLength(1000);

            // --------------------------------------------------------------------
            // Valores sugeridos
            // --------------------------------------------------------------------

            builder.Property(x => x.DefaultMovementType)
                .HasConversion<int>();

            builder.Property(x => x.DefaultFinancialImpact)
                .HasConversion<int>();

            builder.HasOne(x => x.DefaultCategory)
                .WithMany()
                .HasForeignKey(x => x.DefaultCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // --------------------------------------------------------------------
            // Estado
            // --------------------------------------------------------------------

            builder.Property(x => x.IsDeactivated)
                .IsRequired();

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .IsRequired();

            // --------------------------------------------------------------------
            // Índices
            // --------------------------------------------------------------------

            builder.HasIndex(x => x.Name);

            builder.HasIndex(x => x.Type);

            builder.HasIndex(x => x.IsDeactivated);

            builder.HasIndex(x => x.DefaultCategoryId);

            builder.HasIndex(x => new
            {
                x.IsDeactivated,
                x.Name
            });

            builder.HasIndex(x => new
            {
                x.Type,
                x.Name
            });
        }
    }
}
