using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdToolEntityConfiguration : IEntityTypeConfiguration<HouseholdToolEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdToolEntity> builder)
    {
        builder.ToTable("HouseholdTool", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.ToolId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.ToolsOnHand)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tool)
            .WithMany()
            .HasForeignKey(e => e.ToolId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
