using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class IngredientWordingRejectionEntityConfiguration : IEntityTypeConfiguration<IngredientWordingRejectionEntity>
{
    public void Configure(EntityTypeBuilder<IngredientWordingRejectionEntity> builder)
    {
        builder.ToTable("IngredientWordingRejection", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Wording).IsRequired().HasMaxLength(511);
        builder.Property(e => e.Verdict).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(1023);
        builder.Property(e => e.Source).IsRequired().HasMaxLength(255);
        builder.Property(e => e.EvidenceKey).HasMaxLength(128);

        builder.HasIndex(e => e.Wording);
        builder.HasIndex(e => e.IngredientId);
    }
}
