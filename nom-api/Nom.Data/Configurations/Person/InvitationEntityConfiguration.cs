using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Person;

namespace Nom.Data.Configurations.Person;

public class InvitationEntityConfiguration : IEntityTypeConfiguration<InvitationEntity>
{
    public void Configure(EntityTypeBuilder<InvitationEntity> builder)
    {
        builder.ToTable("Invitation", schema: "person");

        // Properties
        builder.Property(e => e.Code).IsRequired().HasMaxLength(50);
        builder.Property(e => e.InviterPersonId).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2047);
        builder.Property(e => e.InvitationType).HasMaxLength(255);

        // Indexes
        builder.HasIndex(e => e.Code).IsUnique();

        // Relationships
        builder.HasOne(e => e.Inviter)
            .WithMany()
            .HasForeignKey(e => e.InviterPersonId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Invitee)
            .WithMany()
            .HasForeignKey(e => e.InviteePersonId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Plan)
            .WithMany()
            .HasForeignKey(e => e.PlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
