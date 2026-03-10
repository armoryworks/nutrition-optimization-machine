using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingListGroupEntityConfiguration : IEntityTypeConfiguration<ShoppingListGroupEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingListGroupEntity> builder)
    {
        builder.ToTable("ShoppingListGroup", schema: "shopping");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.Slug).HasMaxLength(255);
    }
}
