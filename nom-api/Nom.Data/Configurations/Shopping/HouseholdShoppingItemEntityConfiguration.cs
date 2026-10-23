using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class HouseholdShoppingItemEntityConfiguration : IEntityTypeConfiguration<HouseholdShoppingItemEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdShoppingItemEntity> builder)
    {
        builder.ToTable("HouseholdShoppingItem", schema: "shopping");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(e => e.Unit).HasMaxLength(32);
        builder.Property(e => e.Checked).IsRequired().HasDefaultValue(false);

        builder.HasIndex(e => e.HouseholdId);
        builder.HasIndex(e => e.IngredientId).HasFilter("\"IngredientId\" IS NOT NULL");

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CheckedByPerson)
            .WithMany()
            .HasForeignKey(e => e.CheckedByPersonId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
