using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class RestrictionEntityConfiguration : IEntityTypeConfiguration<RestrictionEntity>
{
    public void Configure(EntityTypeBuilder<RestrictionEntity> builder)
    {
        builder.ToTable("Restriction", schema: "plan", t =>
        {
            t.HasCheckConstraint("CHK_Restriction_PersonOrPlan",
                "\"PersonId\" IS NOT NULL OR \"PlanId\" IS NOT NULL");
        });

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.BeginDate).HasColumnType("date");
        builder.Property(e => e.EndDate).HasColumnType("date");
        builder.Property(e => e.Locked).HasDefaultValue(false);
        builder.Property(e => e.LockedBy).HasMaxLength(100);

        // Relationships
        builder.HasOne(e => e.Plan)
            .WithMany(p => p.Restrictions)
            .HasForeignKey(e => e.PlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.RestrictionType)
            .WithMany()
            .HasForeignKey(e => e.RestrictionTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Nutrient)
            .WithMany()
            .HasForeignKey(e => e.NutrientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
