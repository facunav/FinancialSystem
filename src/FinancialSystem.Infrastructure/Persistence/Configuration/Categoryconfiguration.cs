using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialSystem.Infrastructure.Persistence.Configuration
{
    internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.ToTable("Categories");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedOnAdd();

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(c => c.DisplayName)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(c => c.SortOrder)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(c => c.IsSystem)
                .IsRequired()
                .HasDefaultValue(true);

            // Name es la clave técnica — debe ser única
            builder.HasIndex(c => c.Name)
                .IsUnique()
                .HasDatabaseName("UX_Categories_Name");
        }
    }
}
