using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdEntityConfiguration : IEntityTypeConfiguration<HouseholdEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdEntity> builder)
    {
        builder.ToTable("Household", schema: "plan");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Slug).HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);

        // Relationships
        builder.Property(e => e.HouseholdGroupId).IsRequired();
        builder.HasOne(e => e.HouseholdGroup)
            .WithMany()
            .HasForeignKey(e => e.HouseholdGroupId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Members)
            .WithMany();

        builder.HasMany(e => e.Plans)
            .WithMany();

        builder.HasMany(e => e.Preferences)
            .WithOne(p => p.Household)
            .HasForeignKey(p => p.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.InviteTokens)
            .WithOne(t => t.Household)
            .HasForeignKey(t => t.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Webhooks)
            .WithOne(w => w.Household)
            .HasForeignKey(w => w.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.EventNotifiers)
            .WithOne(n => n.Household)
            .HasForeignKey(n => n.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.RecipeActions)
            .WithOne(a => a.Household)
            .HasForeignKey(a => a.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Cookbooks)
            .WithOne(c => c.Household)
            .HasForeignKey(c => c.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.IngredientsOnHand)
            .WithOne(i => i.Household)
            .HasForeignKey(i => i.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ToolsOnHand)
            .WithOne(t => t.Household)
            .HasForeignKey(t => t.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.MadeRecipes)
            .WithMany();
    }
}
