using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeToolEntityConfiguration : IEntityTypeConfiguration<RecipeToolEntity>
{
    public void Configure(EntityTypeBuilder<RecipeToolEntity> builder)
    {
        builder.ToTable("RecipeTool", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.ToolId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.RecipeTools)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tool)
            .WithMany()
            .HasForeignKey(e => e.ToolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
