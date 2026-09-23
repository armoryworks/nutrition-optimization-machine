using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MealPlanRuleEntityConfiguration : IEntityTypeConfiguration<MealPlanRuleEntity>
{
    public void Configure(EntityTypeBuilder<MealPlanRuleEntity> builder)
    {
        builder.ToTable("MealPlanRule", schema: "plan");

        // Properties. Meal type and day are optional: null means the rule
        // applies to any meal / any day.
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.QueryFilter).HasMaxLength(2047);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.MealType)
            .WithMany()
            .HasForeignKey(e => e.MealTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DayOfWeek)
            .WithMany()
            .HasForeignKey(e => e.DayOfWeekId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
