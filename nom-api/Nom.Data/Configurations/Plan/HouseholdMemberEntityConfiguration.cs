using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdMemberEntityConfiguration : IEntityTypeConfiguration<HouseholdMemberEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdMemberEntity> builder)
    {
        builder.ToTable("HouseholdMember", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.PersonId).IsRequired();
        builder.Property(e => e.Role).IsRequired().HasMaxLength(50);

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

        // A person belongs to a household at most once. Join/add check first, but
        // two concurrent redemptions both passed that check in the wild (a real
        // duplicate landed in prod) — the constraint is the actual guarantee.
        builder.HasIndex(e => new { e.HouseholdId, e.PersonId })
            .IsUnique()
            .HasDatabaseName("IX_HouseholdMember_HouseholdId_PersonId");
    }
}
