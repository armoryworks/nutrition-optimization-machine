using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeVariationEntityConfiguration : IEntityTypeConfiguration<RecipeVariationEntity>
{
    public void Configure(EntityTypeBuilder<RecipeVariationEntity> builder)
    {
        builder.ToTable("RecipeVariation", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.PersonId).IsRequired();

        // One saved default variation per person per recipe.
        builder.HasIndex(e => new { e.RecipeId, e.PersonId }).IsUnique();

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RecipeVariationItemEntityConfiguration : IEntityTypeConfiguration<RecipeVariationItemEntity>
{
    public void Configure(EntityTypeBuilder<RecipeVariationItemEntity> builder)
    {
        builder.ToTable("RecipeVariationItem", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RecipeVariationId).IsRequired();
        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.SubstituteIngredientId).IsRequired();
        builder.Property(e => e.Quantity).IsRequired().HasPrecision(10, 4);

        // Each original ingredient is swapped at most once per variation.
        builder.HasIndex(e => new { e.RecipeVariationId, e.IngredientId }).IsUnique();

        builder.HasOne(e => e.RecipeVariation)
            .WithMany(v => v.Items)
            .HasForeignKey(e => e.RecipeVariationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubstituteIngredient)
            .WithMany()
            .HasForeignKey(e => e.SubstituteIngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
