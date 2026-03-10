using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Measurement;

namespace Nom.Data.Configurations.Measurement;

public class MeasurementCategoryEntityConfiguration : IEntityTypeConfiguration<MeasurementCategoryEntity>
{
    public void Configure(EntityTypeBuilder<MeasurementCategoryEntity> builder)
    {
        builder.ToTable("MeasurementCategory", schema: "measurement");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(e => e.BaseUnit)
            .WithMany()
            .HasForeignKey(e => e.BaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
