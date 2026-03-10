using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Nutrient;

namespace Nom.Data.Configurations.Nutrient;

public class NutrientGuidelineEntityConfiguration : IEntityTypeConfiguration<NutrientGuidelineEntity>
{
    public void Configure(EntityTypeBuilder<NutrientGuidelineEntity> builder)
    {
        builder.ToTable("NutrientGuideline", schema: "nutrient");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.GoalTypeId)
            .IsRequired();

        builder.Property(e => e.MinAmount)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.MaxAmount)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.RecommendedAmount)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.Notes)
            .HasColumnType("text");

        // Relationships
        builder.HasOne(e => e.Nutrient)
            .WithMany(n => n.Guidelines)
            .HasForeignKey(e => e.NutrientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.GoalType)
            .WithMany()
            .HasForeignKey(e => e.GoalTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
