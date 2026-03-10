using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListLabelEntityConfiguration : IEntityTypeConfiguration<ShoppingListLabelEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListLabelEntity> builder)
    {
        builder.ToTable("ShoppingListLabel", schema: "shopping");

        // Relationships
        builder.Property(e => e.ShoppingListId).IsRequired();
        builder.HasOne(e => e.ShoppingList)
            .WithMany(sl => sl.Labels)
            .HasForeignKey(e => e.ShoppingListId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.LabelId).IsRequired();
        builder.HasOne(e => e.Label)
            .WithMany()
            .HasForeignKey(e => e.LabelId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
