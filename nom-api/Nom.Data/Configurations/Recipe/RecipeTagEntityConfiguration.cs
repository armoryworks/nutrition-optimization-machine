using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeTagEntityConfiguration : IEntityTypeConfiguration<RecipeTagEntity>
{
    public void Configure(EntityTypeBuilder<RecipeTagEntity> builder)
    {
        builder.ToTable("RecipeTag", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.TagId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.RecipeTags)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
