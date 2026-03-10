using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Measurement;

namespace Nom.Data.Configurations.Measurement;

public class MeasurementEntityConfiguration : IEntityTypeConfiguration<MeasurementEntity>
{
    public void Configure(EntityTypeBuilder<MeasurementEntity> builder)
    {
        builder.ToTable("Measurement", schema: "measurement");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // TPH Discriminator
        builder.HasDiscriminator<string>("MeasurementType")
            .HasValue<BaseMeasurementEntity>("Base")
            .HasValue<IngredientMeasurementEntity>("Ingredient")
            .HasValue<NutrientMeasurementEntity>("Nutrient");

        // Shared properties (from abstract _MeasurementEntity)
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Symbol)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.MeasurementCategoryId)
            .IsRequired();

        builder.Property(e => e.IsBaseUnit)
            .IsRequired();

        builder.Property(e => e.BaseUnitConversionFactor)
            .HasColumnType("decimal(18,6)");

        // Relationships
        builder.HasOne(e => e.Category)
            .WithMany(c => c.Measurements)
            .HasForeignKey(e => e.MeasurementCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
