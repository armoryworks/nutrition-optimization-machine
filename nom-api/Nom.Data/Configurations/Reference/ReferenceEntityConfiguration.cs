using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Reference;

namespace Nom.Data.Configurations.Reference;

public class ReferenceEntityConfiguration : IEntityTypeConfiguration<ReferenceEntity>
{
    public void Configure(EntityTypeBuilder<ReferenceEntity> builder)
    {
        builder.ToTable("Reference", schema: "reference");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.Name).IsRequired();

        // Many-to-many: Reference <-> ReferenceGroup via join table ReferenceIndex
        builder.HasMany(r => r.Groups)
            .WithMany(g => g.References)
            .UsingEntity<Dictionary<string, object>>(
                "ReferenceIndex",
                j => j.HasOne<ReferenceGroupEntity>().WithMany().HasForeignKey("GroupId")
                    .HasConstraintName("FK_ReferenceIndex_ReferenceGroupEntity_GroupId"),
                j => j.HasOne<ReferenceEntity>().WithMany().HasForeignKey("ReferenceId")
                    .HasConstraintName("FK_ReferenceIndex_ReferenceEntity_ReferenceId"),
                j =>
                {
                    j.ToTable("ReferenceIndex", "reference");
                    j.HasKey("ReferenceId", "GroupId");
                });
    }
}
