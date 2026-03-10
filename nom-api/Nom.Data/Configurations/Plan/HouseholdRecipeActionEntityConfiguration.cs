using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class HouseholdRecipeActionEntityConfiguration : IEntityTypeConfiguration<HouseholdRecipeActionEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdRecipeActionEntity> builder)
    {
        builder.ToTable("HouseholdRecipeAction", schema: "plan");

        // Properties
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.ActorId).IsRequired();
        builder.Property(e => e.ActionType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.Details).HasColumnType("text");

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany(h => h.RecipeActions)
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
