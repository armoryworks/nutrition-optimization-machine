using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class RestrictionCriterionEntityConfiguration : IEntityTypeConfiguration<RestrictionCriterionEntity>
{
    public void Configure(EntityTypeBuilder<RestrictionCriterionEntity> builder)
    {
        builder.ToTable("RestrictionCriterion", schema: "plan");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RestrictionTypeId).IsRequired();
        builder.Property(e => e.IngredientPattern).HasMaxLength(255);
        builder.Property(e => e.Severity).IsRequired().HasDefaultValue(3);
        builder.Property(e => e.Notes).HasMaxLength(1023);
        builder.Property(e => e.MaxAmountPerServing).HasPrecision(12, 4);

        builder.HasIndex(e => e.RestrictionTypeId);

        builder.HasOne(e => e.RestrictionType)
            .WithMany()
            .HasForeignKey(e => e.RestrictionTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Nutrient)
            .WithMany()
            .HasForeignKey(e => e.NutrientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
