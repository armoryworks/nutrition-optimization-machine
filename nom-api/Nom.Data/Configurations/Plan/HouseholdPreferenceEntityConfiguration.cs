using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdPreferenceEntityConfiguration : IEntityTypeConfiguration<HouseholdPreferenceEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdPreferenceEntity> builder)
    {
        builder.ToTable("HouseholdPreference", schema: "plan");

        // One row per (household, key) — preferences are upserted by key.
        builder.HasIndex(e => new { e.HouseholdId, e.PreferenceKey }).IsUnique();

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.PreferenceKey).IsRequired().HasMaxLength(255);
        builder.Property(e => e.PreferenceValue).HasColumnType("text");
        builder.Property(e => e.DataType).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.Preferences)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
