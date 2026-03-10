using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Measurement;

namespace Nom.Data.Configurations.Measurement;

public class MeasurementConversionEntityConfiguration : IEntityTypeConfiguration<MeasurementConversionEntity>
{
    public void Configure(EntityTypeBuilder<MeasurementConversionEntity> builder)
    {
        builder.ToTable("MeasurementConversion", schema: "measurement");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.FromMeasurementId)
            .IsRequired();

        builder.Property(e => e.ToMeasurementId)
            .IsRequired();

        builder.Property(e => e.ConversionFactor)
            .IsRequired()
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.Offset)
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.Formula)
            .HasMaxLength(100);

        builder.Property(e => e.IsDirectConversion)
            .IsRequired();

        // Relationships
        builder.HasOne(e => e.FromMeasurement)
            .WithMany()
            .HasForeignKey(e => e.FromMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToMeasurement)
            .WithMany()
            .HasForeignKey(e => e.ToMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
