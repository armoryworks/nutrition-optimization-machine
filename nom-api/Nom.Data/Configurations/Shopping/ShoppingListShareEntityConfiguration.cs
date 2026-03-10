using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListShareEntityConfiguration : IEntityTypeConfiguration<ShoppingListShareEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListShareEntity> builder)
    {
        builder.ToTable("ShoppingListShares", schema: "shopping");

        // Relationships
        builder.HasOne(e => e.ShoppingList)
            .WithMany()
            .HasForeignKey(e => e.ShoppingListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
