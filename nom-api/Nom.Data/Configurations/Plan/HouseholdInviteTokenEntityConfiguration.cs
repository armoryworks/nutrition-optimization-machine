using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdInviteTokenEntityConfiguration : IEntityTypeConfiguration<HouseholdInviteTokenEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdInviteTokenEntity> builder)
    {
        builder.ToTable("HouseholdInviteToken", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.Token).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(50).HasDefaultValue(InviteTokenKinds.HouseholdJoin);
        builder.Property(e => e.ManagedBy).HasMaxLength(100);
        builder.Property(e => e.TemplateRef).HasMaxLength(200);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.InviteTokens)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
