using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class BudgetEntityConfiguration : IEntityTypeConfiguration<BudgetEntity>
{
    public void Configure(EntityTypeBuilder<BudgetEntity> builder)
    {
        builder.ToTable("Budget", schema: "plan", t =>
            t.HasCheckConstraint(
                "CK_Budget_single_owner",
                "(\"PersonId\" IS NULL) <> (\"HouseholdId\" IS NULL)"));

        builder.Property(e => e.Amount).HasColumnType("decimal(10,2)");
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(e => e.Period).IsRequired().HasMaxLength(16).HasDefaultValue("weekly");

        builder.HasIndex(e => e.PersonId).IsUnique().HasFilter("\"PersonId\" IS NOT NULL");
        builder.HasIndex(e => e.HouseholdId).IsUnique().HasFilter("\"HouseholdId\" IS NOT NULL");

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
