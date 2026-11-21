using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientContentsEntityConfiguration : IEntityTypeConfiguration<IngredientContentsEntity>
{
    public void Configure(EntityTypeBuilder<IngredientContentsEntity> builder)
    {
        builder.ToTable("IngredientContents", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.Source).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Statement).IsRequired().HasColumnType("text");
        builder.Property(e => e.Outcome).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Reason).HasMaxLength(255);
        builder.Property(e => e.ReaderVersion).IsRequired().HasMaxLength(64);
        builder.Property(e => e.IngredientsJson).HasColumnType("jsonb");
        builder.Property(e => e.AllergensJson).HasColumnType("jsonb");
        builder.Property(e => e.Unresolved).IsRequired();
        builder.Property(e => e.Batch).IsRequired().HasMaxLength(64);

        builder.HasIndex(e => new { e.IngredientId, e.HouseholdId });
        builder.HasIndex(e => e.Batch);
        builder.HasIndex(e => e.ReadFromId);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
