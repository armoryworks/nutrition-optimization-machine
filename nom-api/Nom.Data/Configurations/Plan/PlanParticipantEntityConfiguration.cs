using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class PlanParticipantEntityConfiguration : IEntityTypeConfiguration<PlanParticipantEntity>
{
    public void Configure(EntityTypeBuilder<PlanParticipantEntity> builder)
    {
        builder.ToTable("PlanParticipant", schema: "plan");

        // Composite key
        builder.HasKey(e => new { e.PlanId, e.PersonId });

        // Properties
        builder.Property(e => e.PlanId).IsRequired();
        builder.Property(e => e.PersonId).IsRequired();
        builder.Property(e => e.RoleRefId).IsRequired();
        builder.Property(e => e.JoinedDate).IsRequired();

        // Relationships
        builder.HasOne(e => e.Plan)
            .WithMany(p => p.Participants)
            .HasForeignKey(e => e.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany(p => p.PlanParticipations)
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleRefId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
