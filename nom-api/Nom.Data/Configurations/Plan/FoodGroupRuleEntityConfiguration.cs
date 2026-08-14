using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class FoodGroupRuleEntityConfiguration : IEntityTypeConfiguration<FoodGroupRuleEntity>
{
    public void Configure(EntityTypeBuilder<FoodGroupRuleEntity> builder)
    {
        builder.ToTable("FoodGroupRule", schema: "plan");

        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.FoodGroupId).IsRequired();
        builder.Property(e => e.MinServings).HasColumnType("decimal(6,2)").IsRequired();
        builder.Property(e => e.Timeframe).HasConversion<int>().IsRequired();
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

        // One rule per (household, food group, timeframe, meal-type scope).
        builder.HasIndex(e => new { e.HouseholdId, e.FoodGroupId, e.Timeframe, e.MealTypeId }).IsUnique();

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.FoodGroup)
            .WithMany()
            .HasForeignKey(e => e.FoodGroupId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.MealType)
            .WithMany()
            .HasForeignKey(e => e.MealTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
