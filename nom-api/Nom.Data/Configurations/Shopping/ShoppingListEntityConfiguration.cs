using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListEntityConfiguration : IEntityTypeConfiguration<ShoppingListEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListEntity> builder)
    {
        builder.ToTable("ShoppingList", schema: "shopping");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);

        // Relationships
        builder.Property(e => e.AuthorId).IsRequired();
        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ShoppingListGroup)
            .WithMany()
            .HasForeignKey(e => e.ShoppingListGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Items)
            .WithOne(i => i.ShoppingList)
            .HasForeignKey(i => i.ShoppingListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Labels)
            .WithOne(l => l.ShoppingList)
            .HasForeignKey(l => l.ShoppingListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
