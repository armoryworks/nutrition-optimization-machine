using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientVerificationEntityConfiguration : IEntityTypeConfiguration<IngredientVerificationEntity>
{
    public void Configure(EntityTypeBuilder<IngredientVerificationEntity> builder)
    {
        builder.ToTable("IngredientVerification", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.IngredientId).IsRequired();
        builder.Property(e => e.Source).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.LabelImagePath).HasMaxLength(512);
        builder.Property(e => e.LabelImageContentType).HasMaxLength(64);
        builder.Property(e => e.OcrText).HasColumnType("text");
        builder.Property(e => e.Upc).HasMaxLength(32);
        builder.Property(e => e.ServingText).HasMaxLength(255);
        builder.Property(e => e.ServingGrams).HasColumnType("decimal(9,2)");
        builder.Property(e => e.ServingMl).HasColumnType("decimal(9,2)");
        builder.Property(e => e.ServingsPerContainer).HasColumnType("decimal(9,2)");
        builder.Property(e => e.PackageGrams).HasColumnType("decimal(9,2)");
        builder.Property(e => e.PackageMl).HasColumnType("decimal(9,2)");
        builder.Property(e => e.PiecesPerPackage).HasColumnType("decimal(9,2)");
        builder.Property(e => e.CupGrams).HasColumnType("decimal(9,2)");
        builder.Property(e => e.NutrientsJson).HasColumnType("jsonb");
        builder.Property(e => e.Confirmed).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.LastAskedDate).IsRequired();

        builder.HasIndex(e => new { e.IngredientId, e.HouseholdId });
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Upc).HasFilter("\"Upc\" IS NOT NULL");
        builder.HasIndex(e => new { e.HouseholdId, e.LastAskedDate });

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
