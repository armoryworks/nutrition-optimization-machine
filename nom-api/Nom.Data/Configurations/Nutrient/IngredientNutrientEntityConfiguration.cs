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
            .HasMaxLength(50);

        // Relationships
        builder.HasOne(e => e.Ingredient)
            .WithMany()
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
