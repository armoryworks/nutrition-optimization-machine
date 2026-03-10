using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientAliasEntityConfiguration : IEntityTypeConfiguration<IngredientAliasEntity>
{
    public void Configure(EntityTypeBuilder<IngredientAliasEntity> builder)
    {
        builder.ToTable("IngredientAlias", schema: "recipe");

        // Composite key
        builder.HasKey(e => new { e.IngredientId, e.AliasName });

        // Properties
        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.AliasName).IsRequired().HasMaxLength(511);
        builder.Property(e => e.SourceContext).HasMaxLength(2047);

        // Relationships
        builder.HasOne(e => e.Ingredient)
            .WithMany(i => i.Aliases)
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
