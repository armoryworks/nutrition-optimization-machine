using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class MacroGoalEntityConfiguration : IEntityTypeConfiguration<MacroGoalEntity>
{
    public void Configure(EntityTypeBuilder<MacroGoalEntity> builder)
    {
        builder.ToTable("MacroGoal", schema: "plan", t =>
            // Exactly one owner: person-scoped XOR household-scoped.
            t.HasCheckConstraint(
                "CK_MacroGoal_single_owner",
                "(\"PersonId\" IS NULL) <> (\"HouseholdId\" IS NULL)"));

        // Properties
        builder.Property(e => e.CaloriesTarget).HasColumnType("decimal(8,1)");
        builder.Property(e => e.ProteinGramsTarget).HasColumnType("decimal(7,1)");
        builder.Property(e => e.CarbGramsTarget).HasColumnType("decimal(7,1)");
        builder.Property(e => e.FatGramsTarget).HasColumnType("decimal(7,1)");

        // One goal row per owner.
        builder.HasIndex(e => e.PersonId).IsUnique().HasFilter("\"PersonId\" IS NOT NULL");
        builder.HasIndex(e => e.HouseholdId).IsUnique().HasFilter("\"HouseholdId\" IS NOT NULL");

        // Relationships
        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
