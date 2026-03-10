using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListItemEntityConfiguration : IEntityTypeConfiguration<ShoppingListItemEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListItemEntity> builder)
    {
        builder.ToTable("ShoppingListItem", schema: "shopping");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(511);
        builder.Property(e => e.Note).HasColumnType("text");
        builder.Property(e => e.Quantity).HasColumnType("decimal(18,4)");

        // Relationships
        builder.Property(e => e.ShoppingListId).IsRequired();
        builder.HasOne(e => e.ShoppingList)
            .WithMany(sl => sl.Items)
            .HasForeignKey(e => e.ShoppingListId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Measurement)
            .WithMany()
            .HasForeignKey(e => e.MeasurementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Category)
            .WithMany(c => c.Items)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
