using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class PlanEntityConfiguration : IEntityTypeConfiguration<PlanEntity>
{
    public void Configure(EntityTypeBuilder<PlanEntity> builder)
    {
        builder.ToTable("Plan", schema: "plan");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.StartDate).IsRequired().HasColumnType("date");
        builder.Property(e => e.EndDate).HasColumnType("date");
        builder.Property(e => e.InvitationCode).HasMaxLength(50);
        builder.Property(e => e.CurationStatusId).IsRequired();
        builder.Property(e => e.AuthorId).IsRequired();
        builder.Property(e => e.Version).IsRequired();

        // Indexes
        builder.HasIndex(e => e.InvitationCode)
            .IsUnique()
            .HasFilter("\"InvitationCode\" IS NOT NULL");

        // Relationships
        builder.HasOne(e => e.CurationStatus)
            .WithMany()
            .HasForeignKey(e => e.CurationStatusId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ParentPlan)
            .WithMany()
            .HasForeignKey(e => e.ParentPlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Restrictions)
            .WithOne(r => r.Plan)
            .HasForeignKey(r => r.PlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Participants)
            .WithOne(pp => pp.Plan)
            .HasForeignKey(pp => pp.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Meals)
            .WithOne(m => m.Plan)
            .HasForeignKey(m => m.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Goals)
            .WithOne(g => g.Plan)
            .HasForeignKey(g => g.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
