using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeRatingEntityConfiguration : IEntityTypeConfiguration<RecipeRatingEntity>
{
    public void Configure(EntityTypeBuilder<RecipeRatingEntity> builder)
    {
        builder.ToTable("RecipeRating", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.RaterId).IsRequired();
        builder.Property(e => e.Rating).IsRequired().HasColumnType("decimal(3,2)");

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Ratings)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Rater)
            .WithMany()
            .HasForeignKey(e => e.RaterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
