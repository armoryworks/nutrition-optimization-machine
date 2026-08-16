using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientEntityConfiguration : IEntityTypeConfiguration<IngredientEntity>
{
    public void Configure(EntityTypeBuilder<IngredientEntity> builder)
    {
        builder.ToTable("Ingredient", schema: "recipe");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(2047);
        builder.Property(e => e.Description).HasColumnType("text");
        builder.Property(e => e.PluralName).HasMaxLength(2047);
        builder.Property(e => e.FdcId).HasMaxLength(255);
        builder.Property(e => e.FdcDataType).HasMaxLength(255);
        builder.Property(e => e.NameNormalized).HasMaxLength(2047);
        builder.Property(e => e.PluralNameNormalized).HasMaxLength(2047);
        builder.Property(e => e.CurationStatusId).IsRequired();

        // Indexes
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.FdcId).IsUnique().HasFilter("\"FdcId\" IS NOT NULL");

        // Relationships
        builder.HasOne(i => i.Author)
            .WithMany()
            .HasForeignKey(i => i.AuthorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.CurationStatus)
            .WithMany()
            .HasForeignKey(i => i.CurationStatusId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Label)
            .WithMany()
            .HasForeignKey(i => i.LabelId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.FoodGroup)
            .WithMany()
            .HasForeignKey(i => i.FoodGroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.FoodGroupId).HasFilter("\"FoodGroupId\" IS NOT NULL");
        builder.HasIndex(e => e.IsWholeFood).HasFilter("\"IsWholeFood\" = true");
        builder.Property(e => e.ReferenceServingGrams).HasColumnType("decimal(9,2)");
        builder.Property(e => e.GtinUpc).HasMaxLength(32);
        builder.HasIndex(e => e.GtinUpc).HasFilter("\"GtinUpc\" IS NOT NULL");
    }
}
