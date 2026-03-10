using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeIngredientEntityConfiguration : IEntityTypeConfiguration<RecipeIngredientEntity>
{
    public void Configure(EntityTypeBuilder<RecipeIngredientEntity> builder)
    {
        builder.ToTable("RecipeIngredient", schema: "recipe");

        // Composite key
        builder.HasKey(e => new { e.RecipeId, e.IngredientId });

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.Quantity).IsRequired().HasColumnType("decimal(18,4)");
        builder.Property(e => e.MeasurementId).IsRequired();
        builder.Property(e => e.RawLine).HasColumnType("Text");

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.RecipeIngredients)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
