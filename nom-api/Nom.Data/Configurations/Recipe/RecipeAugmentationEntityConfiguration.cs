using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeAugmentationEntityConfiguration : IEntityTypeConfiguration<RecipeAugmentationEntity>
{
    public void Configure(EntityTypeBuilder<RecipeAugmentationEntity> builder)
    {
        builder.ToTable("RecipeAugmentation", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(10, 4);
        builder.Property(e => e.FlavorEffect).IsRequired().HasMaxLength(1023);
        builder.Property(e => e.Instructions).HasMaxLength(4095);
        builder.Property(e => e.CurationStatusId).IsRequired();

        builder.HasIndex(e => e.RecipeId);
        builder.HasIndex(e => new { e.RecipeId, e.IngredientId }).IsUnique();

        builder.HasOne(e => e.Recipe)
            .WithMany()
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

        builder.HasOne(e => e.CurationStatus)
            .WithMany()
            .HasForeignKey(e => e.CurationStatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
