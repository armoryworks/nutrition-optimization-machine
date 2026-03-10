using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeShareTokenEntityConfiguration : IEntityTypeConfiguration<RecipeShareTokenEntity>
{
    public void Configure(EntityTypeBuilder<RecipeShareTokenEntity> builder)
    {
        builder.ToTable("RecipeShareToken", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.Token).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Name).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.ShareTokens)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
