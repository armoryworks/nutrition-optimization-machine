using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Nutrient;

namespace Nom.Data.Configurations.Nutrient;

public class NutrientEntityConfiguration : IEntityTypeConfiguration<NutrientEntity>
{
    public void Configure(EntityTypeBuilder<NutrientEntity> builder)
    {
        builder.ToTable("Nutrient", schema: "nutrient");

        // Key + identity (from BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(1023);

        builder.Property(e => e.Rank)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.FdcId)
            .HasMaxLength(50);

        // Relationships
        builder.HasOne(n => n.DefaultMeasurement)
            .WithMany()
            .HasForeignKey(n => n.DefaultMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.ParentNutrient)
            .WithMany(n => n.ChildNutrients)
            .HasForeignKey(n => n.ParentNutrientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes (from OnModelCreating)
        builder.HasIndex(e => new { e.Name, e.DefaultMeasurementId })
            .IsUnique()
            .HasFilter("\"FdcId\" IS NOT NULL");

        builder.HasIndex(e => e.FdcId)
            .IsUnique()
            .HasFilter("\"FdcId\" IS NOT NULL");
    }
}
