using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Reference;

namespace Nom.Data.Configurations.Reference;

public class ReferenceGroupEntityConfiguration : IEntityTypeConfiguration<ReferenceGroupEntity>
{
    public void Configure(EntityTypeBuilder<ReferenceGroupEntity> builder)
    {
        builder.ToTable("Group", schema: "reference");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.Name).IsRequired();
        builder.Property(e => e.Slug).HasMaxLength(255);
    }
}
