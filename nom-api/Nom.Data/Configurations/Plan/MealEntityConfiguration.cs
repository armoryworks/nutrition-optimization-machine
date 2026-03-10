using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MealEntityConfiguration : IEntityTypeConfiguration<MealEntity>
{
    public void Configure(EntityTypeBuilder<MealEntity> builder)
    {
        builder.ToTable("Meal", schema: "plan");

        // Properties
        builder.Property(e => e.PlanId).IsRequired();
        builder.Property(e => e.MealTypeId).IsRequired();
        builder.Property(e => e.Date).IsRequired().HasColumnName("date").HasColumnType("date");

        // Relationships
        builder.HasOne(e => e.Plan)
            .WithMany(p => p.Meals)
            .HasForeignKey(e => e.PlanId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.MealType)
            .WithMany()
            .HasForeignKey(e => e.MealTypeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
