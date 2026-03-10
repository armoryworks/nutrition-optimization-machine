using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingTripEntityConfiguration : IEntityTypeConfiguration<ShoppingTripEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingTripEntity> builder)
    {
        builder.ToTable("ShoppingTrip", schema: "shopping");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.PlannedDate).HasColumnType("date");
        builder.Property(e => e.ActualDate).HasColumnType("date");

        // Relationships
        builder.Property(e => e.PersonId).IsRequired();
        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Status)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Many-to-many with MealEntity (migrated from OnModelCreating)
        builder.HasMany(e => e.Meals)
            .WithMany(m => m.ShoppingTrips)
            .UsingEntity<Dictionary<string, object>>(
                "ShoppingTripMealIndex",
                j => j.HasOne<MealEntity>().WithMany().HasForeignKey("MealId")
                    .HasConstraintName("FK_ShoppingTripMealIndex_MealEntity_MealId"),
                j => j.HasOne<ShoppingTripEntity>().WithMany().HasForeignKey("ShoppingTripId")
                    .HasConstraintName("FK_ShoppingTripMealIndex_ShoppingTripEntity_ShoppingTripId"),
                j =>
                {
                    j.ToTable("shopping_trip_meal_index", "shopping");
                    j.HasKey("ShoppingTripId", "MealId");
                });
    }
}
