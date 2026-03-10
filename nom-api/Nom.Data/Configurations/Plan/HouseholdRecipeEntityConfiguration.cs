using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdRecipeEntityConfiguration : IEntityTypeConfiguration<HouseholdRecipeEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdRecipeEntity> builder)
    {
        builder.ToTable("HouseholdRecipe", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.RecipeId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
