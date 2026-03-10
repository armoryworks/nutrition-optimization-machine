using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Data.Configurations.Recipe;

public class RecipeEntityConfiguration : IEntityTypeConfiguration<RecipeEntity>
{
    public void Configure(EntityTypeBuilder<RecipeEntity> builder)
    {
        builder.ToTable("Recipe", schema: "recipe");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(511);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.TotalTime).HasMaxLength(100);
        builder.Property(e => e.PrepTime).HasMaxLength(100);
        builder.Property(e => e.CookTime).HasMaxLength(100);
        builder.Property(e => e.PerformTime).HasMaxLength(100);
        builder.Property(e => e.RecipeYield).HasMaxLength(100);
        builder.Property(e => e.RecipeYieldQuantity).HasColumnType("decimal(18,2)");
        builder.Property(e => e.RecipeServings).HasColumnType("decimal(18,2)");
        builder.Property(e => e.ServingQuantity).HasColumnType("decimal(18,2)");
        builder.Property(e => e.CurationStatusId).IsRequired();
        builder.Property(e => e.AuthorId).IsRequired();
        builder.Property(e => e.Version).IsRequired();
        builder.Property(e => e.SourceUrl).HasMaxLength(2047);
        builder.Property(e => e.SourceSite).HasMaxLength(255);
        builder.Property(e => e.Rating).HasColumnType("decimal(3,2)");
        builder.Property(e => e.Slug).HasMaxLength(255);
        builder.Property(e => e.Image).HasMaxLength(2047);
        builder.Property(e => e.OrgUrl).HasMaxLength(255);
        builder.Property(e => e.NameNormalized).HasMaxLength(511);
        builder.Property(e => e.DescriptionNormalized).HasMaxLength(2047);

        // Relationships
        builder.HasOne(r => r.ServingQuantityMeasurement)
            .WithMany()
            .HasForeignKey(r => r.ServingQuantityMeasurementId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Author)
            .WithMany()
            .HasForeignKey(r => r.AuthorId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ParentRecipe)
            .WithMany()
            .HasForeignKey(r => r.ParentRecipeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(r => r.CurationStatus)
            .WithMany()
            .HasForeignKey(r => r.CurationStatusId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Many-to-many: Recipe <-> Meal via join table MealRecipeIndex
        builder.HasMany(r => r.Meals)
            .WithMany(m => m.Recipes)
            .UsingEntity<Dictionary<string, object>>(
                "MealRecipeIndex",
                j => j.HasOne<MealEntity>().WithMany().HasForeignKey("MealId")
                    .HasConstraintName("FK_MealRecipeIndex_MealEntity_MealId"),
                j => j.HasOne<RecipeEntity>().WithMany().HasForeignKey("RecipeId")
                    .HasConstraintName("FK_MealRecipeIndex_RecipeEntity_RecipeId"),
                j =>
                {
                    j.ToTable("meal_recipe_index", "plan");
                    j.HasKey("MealId", "RecipeId");
                });

        // Many-to-many: Recipe <-> ReferenceEntity (RecipeTypes) via join table RecipeTypeIndex
        builder.HasMany(r => r.RecipeTypes)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "RecipeTypeIndex",
                j => j.HasOne<ReferenceEntity>().WithMany().HasForeignKey("RecipeTypeId")
                    .HasConstraintName("FK_RecipeTypeIndex_ReferenceEntity_RecipeTypeId"),
                j => j.HasOne<RecipeEntity>().WithMany().HasForeignKey("RecipeId")
                    .HasConstraintName("FK_RecipeTypeIndex_RecipeEntity_RecipeId"),
                j =>
                {
                    j.ToTable("recipe_type_index", "recipe");
                    j.HasKey("RecipeId", "RecipeTypeId");
                });
    }
}
