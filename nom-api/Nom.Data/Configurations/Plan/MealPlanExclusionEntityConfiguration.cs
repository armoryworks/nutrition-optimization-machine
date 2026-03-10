using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MealPlanExclusionEntityConfiguration : IEntityTypeConfiguration<MealPlanExclusionEntity>
{
    public void Configure(EntityTypeBuilder<MealPlanExclusionEntity> builder)
    {
        builder.ToTable("MealPlanExclusion", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.PersonId).IsRequired();
        builder.Property(e => e.Date).IsRequired().HasColumnType("date");

        // Indexes
        builder.HasIndex(e => new { e.HouseholdId, e.PersonId, e.Date, e.MealTypeId })
            .IsUnique()
            .HasFilter(null);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.MealType)
            .WithMany()
            .HasForeignKey(e => e.MealTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
