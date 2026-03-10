using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListCategoryEntityConfiguration : IEntityTypeConfiguration<ShoppingListCategoryEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListCategoryEntity> builder)
    {
        builder.ToTable("ShoppingListCategory", schema: "shopping");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);

        // Relationships
        builder.Property(e => e.HouseholdId).IsRequired();
        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Items)
            .WithOne(i => i.Category)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
