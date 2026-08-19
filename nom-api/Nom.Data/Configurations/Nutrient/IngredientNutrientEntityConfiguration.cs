using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Nutrient;

namespace Nom.Data.Configurations.Nutrient;

public class IngredientNutrientEntityConfiguration : IEntityTypeConfiguration<IngredientNutrientEntity>
{
    public void Configure(EntityTypeBuilder<IngredientNutrientEntity> builder)
    {
        builder.ToTable("IngredientNutrient", schema: "nutrient");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.FdcId)
            .HasMaxLength(255);

        // Relationships
        // Inverse navigation matters: without it EF invents a shadow FK
        // ("IngredientEntityId") for Ingredient.IngredientNutrients and the collection
        // never sees rows written through IngredientId — imports, the ingredient form,
        // and everything reading Ingredient.IngredientNutrients silently saw nothing.
        builder.HasOne(e => e.Ingredient)
            .WithMany(i => i.IngredientNutrients)
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Nutrient)
            .WithMany(n => n.IngredientNutrients)
            .HasForeignKey(e => e.NutrientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes (from OnModelCreating)
        builder.HasIndex(e => new { e.IngredientId, e.NutrientId })
            .IsUnique();
    }
}
