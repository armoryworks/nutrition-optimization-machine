using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdWebhookEntityConfiguration : IEntityTypeConfiguration<HouseholdWebhookEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdWebhookEntity> builder)
    {
        builder.ToTable("HouseholdWebhook", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Url).IsRequired().HasMaxLength(2047);
        builder.Property(e => e.EventType).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.Webhooks)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
