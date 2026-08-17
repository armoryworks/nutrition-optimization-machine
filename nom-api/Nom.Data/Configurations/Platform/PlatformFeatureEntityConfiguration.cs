using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Platform;

namespace Nom.Data.Configurations.Platform;

public class PlatformFeatureEntityConfiguration : IEntityTypeConfiguration<PlatformFeatureEntity>
{
    public void Configure(EntityTypeBuilder<PlatformFeatureEntity> builder)
    {
        builder.ToTable("PlatformFeature", schema: "auth");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Key).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Key).IsUnique();

        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.Description).HasMaxLength(500);
    }
}
