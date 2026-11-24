using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeNutritionCheckEntityConfiguration : IEntityTypeConfiguration<RecipeNutritionCheckEntity>
{
    public void Configure(EntityTypeBuilder<RecipeNutritionCheckEntity> builder)
    {
        builder.ToTable("RecipeNutritionCheck", schema: "recipe");

        builder.HasKey(e => e.RecipeId);
        builder.Property(e => e.RecipeId).ValueGeneratedNever();
    }
}
