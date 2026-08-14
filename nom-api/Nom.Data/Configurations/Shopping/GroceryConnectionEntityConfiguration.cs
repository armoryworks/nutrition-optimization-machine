using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class GroceryConnectionEntityConfiguration : IEntityTypeConfiguration<GroceryConnectionEntity>
{
    public void Configure(EntityTypeBuilder<GroceryConnectionEntity> builder)
    {
        builder.ToTable("GroceryConnection", schema: "shopping");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.PersonId).IsRequired();
        builder.Property(e => e.Provider).IsRequired().HasMaxLength(64);

        // Ciphertext, not the raw tokens — see GroceryConnectionService.
        builder.Property(e => e.AccessToken).IsRequired().HasColumnType("text");
        builder.Property(e => e.RefreshToken).HasColumnType("text");

        builder.Property(e => e.LocationId).HasMaxLength(64);
        builder.Property(e => e.LocationName).HasMaxLength(255);
        builder.Property(e => e.PendingState).HasColumnType("text");

        // One live connection per person per provider.
        builder.HasIndex(e => new { e.PersonId, e.Provider }).IsUnique();

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
