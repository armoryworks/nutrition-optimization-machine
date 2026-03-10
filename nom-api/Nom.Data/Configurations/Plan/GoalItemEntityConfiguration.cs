using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class GoalItemEntityConfiguration : IEntityTypeConfiguration<GoalItemEntity>
{
    public void Configure(EntityTypeBuilder<GoalItemEntity> builder)
    {
        builder.ToTable("GoalItem", schema: "plan");

        // Properties
        builder.Property(e => e.GoalId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(2047);
        builder.Property(e => e.IsQuantifiable).IsRequired();
        builder.Property(e => e.MeasurementMinimum).HasColumnType("decimal(18,2)");
        builder.Property(e => e.MeasurementMaximum).HasColumnType("decimal(18,2)");

        // Relationships
        builder.HasOne(e => e.Goal)
            .WithMany(g => g.GoalItems)
            .HasForeignKey(e => e.GoalId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Nutrient)
            .WithMany()
            .HasForeignKey(e => e.NutrientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TimeframeType)
            .WithMany()
            .HasForeignKey(e => e.TimeframeTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
