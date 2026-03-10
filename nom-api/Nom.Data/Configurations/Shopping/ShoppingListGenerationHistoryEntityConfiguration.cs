using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListGenerationHistoryEntityConfiguration : IEntityTypeConfiguration<ShoppingListGenerationHistoryEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListGenerationHistoryEntity> builder)
    {
        builder.ToTable("ShoppingListGenerationHistory", schema: "shopping");

        // Properties
        builder.Property(e => e.ShoppingListId).IsRequired();
        builder.Property(e => e.GeneratedDate).IsRequired();
        builder.Property(e => e.GenerationMethod).IsRequired().HasMaxLength(50);
        builder.Property(e => e.RecipeCount).IsRequired();
        builder.Property(e => e.ItemCount).IsRequired();
        builder.Property(e => e.EstimatedCost).HasColumnType("decimal(10,2)");
        builder.Property(e => e.OptimizationApplied).IsRequired();
        builder.Property(e => e.OptimizationDetails).HasColumnType("text");
        builder.Property(e => e.GeneratedItems).HasColumnType("text");
        builder.Property(e => e.ExcludedItems).HasColumnType("text");
        builder.Property(e => e.SubstitutionsApplied).HasColumnType("text");
    }
}
