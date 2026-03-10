using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Reference;

namespace Nom.Data.Configurations.Reference;

public class RetailPackagingEntityConfiguration : IEntityTypeConfiguration<RetailPackagingEntity>
{
    public void Configure(EntityTypeBuilder<RetailPackagingEntity> builder)
    {
        builder.ToTable("RetailPackaging", schema: "reference");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.IngredientPattern).IsRequired().HasMaxLength(200);
        builder.Property(e => e.PackageName).IsRequired().HasMaxLength(50);
        builder.Property(e => e.PackageSize).HasColumnType("decimal(10,2)");
        builder.Property(e => e.PackageSizeUnit).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SizeCategory).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SizeInBaseUnits).HasColumnType("decimal(12,4)");
        builder.Property(e => e.Source).IsRequired().HasMaxLength(50);
    }
}
