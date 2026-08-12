using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientComponentEntityConfiguration : IEntityTypeConfiguration<IngredientComponentEntity>
{
    public void Configure(EntityTypeBuilder<IngredientComponentEntity> builder)
    {
        builder.ToTable("IngredientComponent", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.ComponentIngredientId).IsRequired();
        builder.Property(e => e.SortOrder).IsRequired().HasDefaultValue(0);

        // A component appears at most once per composite ingredient.
        builder.HasIndex(e => new { e.IngredientId, e.ComponentIngredientId }).IsUnique();

        builder.HasOne(e => e.Ingredient)
            .WithMany(i => i.Components)
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not cascade: deleting a plain ingredient that other
        // composites reference must be an explicit decision, and a second
        // cascade path onto the same table would be ambiguous anyway.
        builder.HasOne(e => e.ComponentIngredient)
            .WithMany()
            .HasForeignKey(e => e.ComponentIngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
