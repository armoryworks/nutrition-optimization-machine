using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class GoalEntityConfiguration : IEntityTypeConfiguration<GoalEntity>
{
    public void Configure(EntityTypeBuilder<GoalEntity> builder)
    {
        builder.ToTable("Goal", schema: "plan");

        // Properties
        builder.Property(e => e.PlanId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(2047);
        builder.Property(e => e.BeginDate).HasColumnType("date");
        builder.Property(e => e.EndDate).HasColumnType("date");

        // Relationships
        builder.HasOne(e => e.Plan)
            .WithMany(p => p.Goals)
            .HasForeignKey(e => e.PlanId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.GoalType)
            .WithMany()
            .HasForeignKey(e => e.GoalTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.GoalItems)
            .WithOne(gi => gi.Goal)
            .HasForeignKey(gi => gi.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
