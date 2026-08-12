using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeSubstitutionEntityConfiguration : IEntityTypeConfiguration<RecipeSubstitutionEntity>
{
    public void Configure(EntityTypeBuilder<RecipeSubstitutionEntity> builder)
    {
        builder.ToTable("RecipeSubstitution", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.SubstituteIngredientId).IsRequired();
        builder.Property(e => e.Ratio).IsRequired().HasDefaultValue(1m).HasPrecision(10, 4);
        builder.Property(e => e.SubstituteQuantity).HasPrecision(10, 4);
        builder.Property(e => e.Notes).HasMaxLength(1023);
        builder.Property(e => e.CurationStatusId).IsRequired();

        builder.HasIndex(e => e.RecipeId);
        builder.HasIndex(e => new { e.RecipeId, e.IngredientId, e.SubstituteIngredientId }).IsUnique();

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubstituteIngredient)
            .WithMany()
            .HasForeignKey(e => e.SubstituteIngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubstituteMeasurement)
            .WithMany()
            .HasForeignKey(e => e.SubstituteMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CurationStatus)
            .WithMany()
            .HasForeignKey(e => e.CurationStatusId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
