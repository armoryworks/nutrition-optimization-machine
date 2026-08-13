using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MemberPolicyEntityConfiguration : IEntityTypeConfiguration<MemberPolicyEntity>
{
    public void Configure(EntityTypeBuilder<MemberPolicyEntity> builder)
    {
        builder.ToTable("MemberPolicy", schema: "plan");

        builder.Property(e => e.FeatureGates).IsRequired().HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(e => e.FrequencyCaps).IsRequired().HasColumnType("jsonb").HasDefaultValue("[]");
        builder.Property(e => e.CuratedOnly).HasDefaultValue(false);
        builder.Property(e => e.UpdatedBy).HasMaxLength(100);

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.HouseholdId, e.PersonId }).IsUnique();
    }
}

public class EnrollmentEventEntityConfiguration : IEntityTypeConfiguration<EnrollmentEventEntity>
{
    public void Configure(EntityTypeBuilder<EnrollmentEventEntity> builder)
    {
        builder.ToTable("EnrollmentEvent", schema: "plan");

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.ManagedBy).HasMaxLength(100);
        builder.Property(e => e.TemplateRef).HasMaxLength(200);

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.InviteToken)
            .WithMany()
            .HasForeignKey(e => e.InviteTokenId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // The external manager's poll: unprocessed events, oldest first.
        builder.HasIndex(e => e.ProcessedAt);
        builder.HasIndex(e => e.HouseholdId);
    }
}

public class PolicyContractVersionEntityConfiguration : IEntityTypeConfiguration<PolicyContractVersionEntity>
{
    public void Configure(EntityTypeBuilder<PolicyContractVersionEntity> builder)
    {
        builder.ToTable("PolicyContractVersion", schema: "plan");
    }
}
