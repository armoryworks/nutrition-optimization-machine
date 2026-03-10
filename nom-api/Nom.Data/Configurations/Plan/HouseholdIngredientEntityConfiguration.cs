using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdIngredientEntityConfiguration : IEntityTypeConfiguration<HouseholdIngredientEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdIngredientEntity> builder)
    {
        builder.ToTable("HouseholdIngredient", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.IngredientId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.IngredientsOnHand)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
