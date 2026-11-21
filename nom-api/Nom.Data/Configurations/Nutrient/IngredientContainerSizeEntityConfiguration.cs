using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Nutrient;

namespace Nom.Data.Configurations.Nutrient;

public class IngredientContainerSizeEntityConfiguration : IEntityTypeConfiguration<IngredientContainerSizeEntity>
{
    public void Configure(EntityTypeBuilder<IngredientContainerSizeEntity> builder)
    {
        builder.ToTable("IngredientContainerSize", schema: "nutrient");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.ContainerKind).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Era).IsRequired().HasMaxLength(16);
        builder.Property(e => e.SizeWord).IsRequired().HasMaxLength(16);
        builder.Property(e => e.NetGrams).HasPrecision(10, 2);
        builder.Property(e => e.DrainedGrams).HasPrecision(10, 2);
        builder.Property(e => e.SizeText).IsRequired().HasMaxLength(255);
        builder.Property(e => e.SourceRef).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Class).IsRequired().HasMaxLength(64);
        builder.Property(e => e.SourceAttribution).HasMaxLength(1023);
        builder.Property(e => e.Evidence).HasMaxLength(1023);
        builder.Property(e => e.ModeShare).HasPrecision(5, 4);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(16);
        builder.Property(e => e.StatusReason).HasMaxLength(1023);

        builder.HasIndex(e => new { e.IngredientId, e.ContainerKind, e.Era, e.SizeWord });
        builder.HasIndex(e => e.Status);
    }
}
