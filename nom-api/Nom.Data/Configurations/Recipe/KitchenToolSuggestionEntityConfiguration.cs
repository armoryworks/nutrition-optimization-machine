using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class KitchenToolSuggestionEntityConfiguration : IEntityTypeConfiguration<KitchenToolSuggestionEntity>
{
    public void Configure(EntityTypeBuilder<KitchenToolSuggestionEntity> builder)
    {
        builder.ToTable("KitchenToolSuggestion", schema: "recipe");

        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.SuggestedCategory).HasMaxLength(100);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => new { e.Status, e.SeenCount });

        builder.HasOne(e => e.Tool)
            .WithMany()
            .HasForeignKey(e => e.ToolId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class KitchenToolSuggestionRecipeEntityConfiguration : IEntityTypeConfiguration<KitchenToolSuggestionRecipeEntity>
{
    public void Configure(EntityTypeBuilder<KitchenToolSuggestionRecipeEntity> builder)
    {
        builder.ToTable("KitchenToolSuggestionRecipe", schema: "recipe");

        builder.HasIndex(e => new { e.SuggestionId, e.RecipeId }).IsUnique();

        builder.HasOne(e => e.Suggestion)
            .WithMany(s => s.Recipes)
            .HasForeignKey(e => e.SuggestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
