using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdEventNotifierEntityConfiguration : IEntityTypeConfiguration<HouseholdEventNotifierEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdEventNotifierEntity> builder)
    {
        builder.ToTable("HouseholdEventNotifier", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.EventType).HasMaxLength(255);
        builder.Property(e => e.NotificationType).HasMaxLength(255);
        builder.Property(e => e.Configuration).HasColumnType("text");

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.EventNotifiers)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
