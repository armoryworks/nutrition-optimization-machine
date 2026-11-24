using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class LineGuardHoldEntityConfiguration : IEntityTypeConfiguration<LineGuardHoldEntity>
{
    public void Configure(EntityTypeBuilder<LineGuardHoldEntity> builder)
    {
        builder.ToTable("LineGuardHold", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Origin).IsRequired().HasMaxLength(32);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(64);
        builder.Property(e => e.RuleVersion).IsRequired().HasMaxLength(64);
        builder.Property(e => e.RawLine).IsRequired().HasColumnType("Text");
        builder.Property(e => e.StoredFood).HasMaxLength(255);
        builder.Property(e => e.StoredQuantity).HasColumnType("decimal(18,4)");
        builder.Property(e => e.StoredUnit).HasMaxLength(64);
        builder.Property(e => e.StoredKind).HasMaxLength(32);
        builder.Property(e => e.RebuiltFood).HasMaxLength(255);
        builder.Property(e => e.RebuiltQuantity).HasColumnType("decimal(18,4)");
        builder.Property(e => e.RebuiltUnit).HasMaxLength(64);
        builder.Property(e => e.RebuiltKind).HasMaxLength(32);
        builder.Property(e => e.Resolution).HasMaxLength(64);

        builder.HasIndex(e => new { e.RecipeId, e.IngredientId, e.SortOrder, e.Origin }).IsUnique().HasFilter("\"ResolvedAt\" IS NULL");
        builder.HasIndex(e => new { e.Reason, e.Origin }).HasFilter("\"ResolvedAt\" IS NULL");
    }
}
