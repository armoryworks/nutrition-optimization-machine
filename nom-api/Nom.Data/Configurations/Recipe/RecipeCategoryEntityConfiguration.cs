using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeCategoryEntityConfiguration : IEntityTypeConfiguration<RecipeCategoryEntity>
{
    public void Configure(EntityTypeBuilder<RecipeCategoryEntity> builder)
    {
        builder.ToTable("RecipeCategory", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.CategoryId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.RecipeCategories)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
