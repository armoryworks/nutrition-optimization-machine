using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MealPlanEntityConfiguration : IEntityTypeConfiguration<MealPlanEntity>
{
    public void Configure(EntityTypeBuilder<MealPlanEntity> builder)
    {
        builder.ToTable("MealPlan", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.AuthorId).IsRequired();
        builder.Property(e => e.Date).IsRequired().HasColumnType("date");
        builder.Property(e => e.MealTypeId).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(2047);
        builder.Property(e => e.Title).HasMaxLength(255);
        builder.Property(e => e.CompletedDate).HasColumnType("date");

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
    }
}
