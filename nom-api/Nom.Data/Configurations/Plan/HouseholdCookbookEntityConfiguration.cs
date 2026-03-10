using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdCookbookEntityConfiguration : IEntityTypeConfiguration<HouseholdCookbookEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdCookbookEntity> builder)
    {
        builder.ToTable("HouseholdCookbook", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.Slug).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.Cookbooks)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Recipes)
            .WithOne(r => r.HouseholdCookbook)
            .HasForeignKey(r => r.HouseholdCookbookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
