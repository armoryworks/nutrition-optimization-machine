using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Measurement;

namespace Nom.Data.Configurations.Measurement;

public class NutrientMeasurementEntityConfiguration : IEntityTypeConfiguration<NutrientMeasurementEntity>
{
    public void Configure(EntityTypeBuilder<NutrientMeasurementEntity> builder)
    {
        // Properties specific to NutrientMeasurementEntity
        builder.Property(e => e.NutrientId)
            .IsRequired();

        builder.Property(e => e.StandardAmount)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.StandardDailyValueUnit)
            .HasMaxLength(50);

        builder.Property(e => e.Notes)
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(e => e.Nutrient)
            .WithMany()
            .HasForeignKey(e => e.NutrientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.StandardMeasurement)
            .WithMany()
            .HasForeignKey(e => e.StandardMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DefaultMeasurement)
            .WithMany()
            .HasForeignKey(e => e.DefaultMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
