using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Shopping;

namespace Nom.Data.Configurations.Shopping;

public class HouseholdShoppingCheckEntityConfiguration : IEntityTypeConfiguration<HouseholdShoppingCheckEntity>
{
    public void Configure(EntityTypeBuilder<HouseholdShoppingCheckEntity> builder)
    {
        builder.ToTable("HouseholdShoppingCheck", schema: "shopping");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.HouseholdId).IsRequired();
        builder.Property(e => e.ItemKey).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Checked).IsRequired().HasDefaultValue(false);

        builder.HasIndex(e => new { e.HouseholdId, e.ItemKey }).IsUnique();
        builder.HasIndex(e => new { e.HouseholdId, e.Checked });

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CheckedByPerson)
            .WithMany()
            .HasForeignKey(e => e.CheckedByPersonId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
