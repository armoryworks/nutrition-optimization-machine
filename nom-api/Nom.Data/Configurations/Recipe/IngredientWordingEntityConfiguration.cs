using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientWordingEntityConfiguration : IEntityTypeConfiguration<IngredientWordingEntity>
{
    public void Configure(EntityTypeBuilder<IngredientWordingEntity> builder)
    {
        builder.ToTable("IngredientWording", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Wording).IsRequired().HasMaxLength(511);
        builder.Property(e => e.Verdict).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Source).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Batch).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Note).HasMaxLength(1023);

        builder.HasIndex(e => e.Wording).IsUnique();
        builder.HasIndex(e => e.IngredientId);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
