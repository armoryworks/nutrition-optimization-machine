using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Curation;

namespace Nom.Data.Configurations.Curation;

public class LaneDecisionEntityConfiguration : IEntityTypeConfiguration<LaneDecisionEntity>
{
    public void Configure(EntityTypeBuilder<LaneDecisionEntity> builder)
    {
        builder.ToTable("LaneDecision", schema: "curation");

        builder.Property(e => e.Lane).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Tier).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Decision).HasMaxLength(255);
        builder.Property(e => e.Mode).HasMaxLength(16).IsRequired();
        builder.Property(e => e.SignatureHash).HasMaxLength(128);
        builder.Property(e => e.Stratum).HasMaxLength(128);
        builder.Property(e => e.SignalsJson).HasColumnType("jsonb");
        builder.Property(e => e.VerdictsJson).HasColumnType("jsonb");
        builder.Property(e => e.AuditOutcome).HasMaxLength(16);
        builder.Property(e => e.AuditJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.Lane, e.Tier, e.CreatedDate });
        builder.HasIndex(e => new { e.Lane, e.Tier, e.AuditedAt }).HasFilter("\"AuditOutcome\" IS NOT NULL");
    }
}
