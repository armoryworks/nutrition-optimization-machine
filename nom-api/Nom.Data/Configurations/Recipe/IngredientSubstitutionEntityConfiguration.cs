using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientSubstitutionEntityConfiguration : IEntityTypeConfiguration<IngredientSubstitutionEntity>
{
    public void Configure(EntityTypeBuilder<IngredientSubstitutionEntity> builder)
    {
        builder.ToTable("IngredientSubstitution", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.SubstituteIngredientId).IsRequired();
        builder.Property(e => e.Ratio).IsRequired().HasDefaultValue(1m).HasPrecision(10, 4);
        builder.Property(e => e.Notes).HasMaxLength(1023);

        builder.HasIndex(e => new { e.IngredientId, e.SubstituteIngredientId }).IsUnique();

        builder.HasOne(e => e.Ingredient)
            .WithMany(i => i.Substitutions)
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SubstituteIngredient)
            .WithMany()
            .HasForeignKey(e => e.SubstituteIngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
