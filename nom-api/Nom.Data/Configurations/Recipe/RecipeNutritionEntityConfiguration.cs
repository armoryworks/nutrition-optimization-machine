using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeNutritionEntityConfiguration : IEntityTypeConfiguration<RecipeNutritionEntity>
{
    public void Configure(EntityTypeBuilder<RecipeNutritionEntity> builder)
    {
        builder.ToTable("RecipeNutrition", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.NutrientId).IsRequired();
        builder.Property(e => e.Amount).IsRequired().HasColumnType("decimal(18,4)");
        builder.Property(e => e.Unit).HasMaxLength(50);
        builder.Property(e => e.DailyValuePercentage).HasColumnType("decimal(18,2)");

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Nutrition)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Nutrient)
            .WithMany()
            .HasForeignKey(e => e.NutrientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
