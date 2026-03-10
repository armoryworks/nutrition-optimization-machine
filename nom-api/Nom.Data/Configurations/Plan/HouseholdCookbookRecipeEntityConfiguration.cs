using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdCookbookRecipeEntityConfiguration : IEntityTypeConfiguration<HouseholdCookbookRecipeEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdCookbookRecipeEntity> builder)
    {
        builder.ToTable("HouseholdCookbookRecipe", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdCookbookId).IsRequired();
        builder.Property(e => e.RecipeId).IsRequired();

        // Relationships
        builder.HasOne(e => e.HouseholdCookbook)
            .WithMany(c => c.Recipes)
            .HasForeignKey(e => e.HouseholdCookbookId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
