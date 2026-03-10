using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeAssetEntityConfiguration : IEntityTypeConfiguration<RecipeAssetEntity>
{
    public void Configure(EntityTypeBuilder<RecipeAssetEntity> builder)
    {
        builder.ToTable("RecipeAsset", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.FileExtension).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Icon).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FileData).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.ContentType).HasMaxLength(100);

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Assets)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
