using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class PantryItemEntityConfiguration : IEntityTypeConfiguration<PantryItemEntity>
{
    public void Configure(EntityTypeBuilder<PantryItemEntity> builder)
    {
        builder.ToTable("PantryItem", schema: "shopping");

        // Properties
        builder.Property(e => e.Quantity).IsRequired().HasColumnType("decimal(18,4)");
        builder.Property(e => e.AcquisitionDate).IsRequired().HasColumnType("date");
        builder.Property(e => e.ExpectedExpirationDate).HasColumnType("date");
        builder.Property(e => e.SourceLocation).HasMaxLength(255);
        builder.Property(e => e.Notes).HasMaxLength(2047);

        // Relationships
        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.PlanId).IsRequired();
        builder.HasOne(e => e.Plan)
            .WithMany()
            .HasForeignKey(e => e.PlanId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ShoppingTrip)
            .WithMany()
            .HasForeignKey(e => e.ShoppingTripId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.IngredientId).IsRequired();
        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.MeasurementId).IsRequired();
        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ItemStatusTypeId).IsRequired();
        builder.HasOne(e => e.ItemStatusType)
            .WithMany()
            .HasForeignKey(e => e.ItemStatusTypeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
