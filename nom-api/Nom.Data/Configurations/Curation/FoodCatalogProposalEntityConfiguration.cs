using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Curation;

namespace Nom.Data.Configurations.Curation;

public class FoodCatalogProposalEntityConfiguration : IEntityTypeConfiguration<FoodCatalogProposalEntity>
{
    public void Configure(EntityTypeBuilder<FoodCatalogProposalEntity> builder)
    {
        builder.ToTable("FoodCatalogProposal", schema: "curation");

        builder.Property(e => e.Action).HasConversion<int>().IsRequired();
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.FdcId).HasMaxLength(255);
        builder.Property(e => e.Field).HasMaxLength(64);
        builder.Property(e => e.CurrentValue).HasMaxLength(2047);
        builder.Property(e => e.ProposedValue).HasMaxLength(2047);
        builder.Property(e => e.Confidence).HasColumnType("decimal(4,3)");
        builder.Property(e => e.Reason).HasMaxLength(2047);
        builder.Property(e => e.Source).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Batch).HasMaxLength(128);

        builder.HasIndex(e => new { e.Status, e.Batch });
        builder.HasIndex(e => e.IngredientId).HasFilter("\"IngredientId\" IS NOT NULL");

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
