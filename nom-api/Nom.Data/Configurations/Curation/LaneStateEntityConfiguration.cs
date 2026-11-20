using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Curation;

namespace Nom.Data.Configurations.Curation;

public class LaneStateEntityConfiguration : IEntityTypeConfiguration<LaneStateEntity>
{
    public void Configure(EntityTypeBuilder<LaneStateEntity> builder)
    {
        builder.ToTable("LaneState", schema: "curation");

        builder.Property(e => e.Lane).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Tier).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Mode).HasMaxLength(16).IsRequired();
        builder.Property(e => e.SignatureHash).HasMaxLength(128);
        builder.Property(e => e.PausedSignatureHash).HasMaxLength(128);
        builder.Property(e => e.FiguresJson).HasColumnType("jsonb");
        builder.Property(e => e.Reason).HasMaxLength(1023);
        builder.Property(e => e.Since).IsRequired();
        builder.Property(e => e.WindowStart).IsRequired();

        builder.HasIndex(e => new { e.Lane, e.Tier }).IsUnique();
    }
}
