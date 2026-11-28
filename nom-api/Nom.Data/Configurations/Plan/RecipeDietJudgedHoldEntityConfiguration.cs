using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class RecipeDietJudgedHoldEntityConfiguration : IEntityTypeConfiguration<RecipeDietJudgedHoldEntity>
{
    public void Configure(EntityTypeBuilder<RecipeDietJudgedHoldEntity> builder)
    {
        builder.ToTable("RecipeDietJudgedHold", schema: "plan");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Food).IsRequired().HasMaxLength(100);
        builder.Property(e => e.JudgedBy).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Evidence).IsRequired().HasMaxLength(255);
        builder.HasIndex(e => new { e.RecipeId, e.RestrictionTypeId }).IsUnique();
        builder.HasIndex(e => e.LoadedAt);
    }
}
