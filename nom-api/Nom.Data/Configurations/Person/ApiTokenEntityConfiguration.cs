using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Person;

namespace Nom.Data.Configurations.Person;

public class ApiTokenEntityConfiguration : IEntityTypeConfiguration<ApiTokenEntity>
{
    public void Configure(EntityTypeBuilder<ApiTokenEntity> builder)
    {
        builder.ToTable("ApiToken", schema: "person");

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.TokenHash).IsUnique();
    }
}
