using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class ShoppingPreferenceEntityConfiguration : IEntityTypeConfiguration<ShoppingPreferenceEntity>
{
    public void Configure(EntityTypeBuilder<ShoppingPreferenceEntity> builder)
    {
        builder.ToTable("ShoppingPreference", schema: "shopping");

        // Relationships
        builder.Property(e => e.PersonId).IsRequired();
        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
