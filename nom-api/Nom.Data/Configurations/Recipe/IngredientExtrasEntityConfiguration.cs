using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientExtrasEntityConfiguration : IEntityTypeConfiguration<IngredientExtrasEntity>
{
    public void Configure(EntityTypeBuilder<IngredientExtrasEntity> builder)
    {
        builder.ToTable("IngredientExtras", schema: "recipe");

        // Properties
        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.Key).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Value).HasColumnType("text");
        builder.Property(e => e.DataType).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Ingredient)
            .WithMany(i => i.Extras)
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
