using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MealPlanEntityConfiguration : IEntityTypeConfiguration<MealPlanEntity>
{
    public void Configure(EntityTypeBuilder<MealPlanEntity> builder)
    {
        builder.ToTable("MealPlan", schema: "plan", t =>
            // A slot targets a recipe XOR a standalone ingredient (or neither, for free-text).
            t.HasCheckConstraint(
                "CK_MealPlan_recipe_xor_ingredient",
                "NOT (\"RecipeId\" IS NOT NULL AND \"IngredientId\" IS NOT NULL)"));

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.AuthorId).IsRequired();
        builder.Property(e => e.Date).IsRequired().HasColumnType("date");
        builder.Property(e => e.MealTypeId).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(2047);
        builder.Property(e => e.Title).HasMaxLength(255);
        builder.Property(e => e.CompletedDate).HasColumnType("date");
        builder.Property(e => e.Quantity).HasColumnType("decimal(9,2)");

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.MealType)
            .WithMany()
            .HasForeignKey(e => e.MealTypeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
