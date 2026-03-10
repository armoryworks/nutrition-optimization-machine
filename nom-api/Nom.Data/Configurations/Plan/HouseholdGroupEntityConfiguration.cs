using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdGroupEntityConfiguration : IEntityTypeConfiguration<HouseholdGroupEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdGroupEntity> builder)
    {
        builder.ToTable("HouseholdGroup", schema: "plan");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.Slug).HasMaxLength(255);
    }
}
