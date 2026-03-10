using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Measurement;

namespace Nom.Data.Configurations.Measurement;

public class IngredientMeasurementEntityConfiguration : IEntityTypeConfiguration<IngredientMeasurementEntity>
{
    public void Configure(EntityTypeBuilder<IngredientMeasurementEntity> builder)
    {
        // Properties specific to IngredientMeasurementEntity
        builder.Property(e => e.IngredientId)
            .IsRequired();

        builder.Property(e => e.TypicalQuantity)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.Notes)
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PreferredMeasurement)
            .WithMany()
            .HasForeignKey(e => e.PreferredMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DefaultMeasurement)
            .WithMany()
            .HasForeignKey(e => e.DefaultMeasurementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
