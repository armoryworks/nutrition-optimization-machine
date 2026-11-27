using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class RecipeDietReadingEntityConfiguration : IEntityTypeConfiguration<RecipeDietReadingEntity>
{
    public void Configure(EntityTypeBuilder<RecipeDietReadingEntity> builder)
    {
        builder.ToTable("RecipeDietReading", schema: "plan");

        builder.HasKey(e => new { e.RecipeId, e.Model });
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.PromptSignature).IsRequired().HasMaxLength(32);
        builder.Property(e => e.TextSignature).IsRequired().HasMaxLength(32);
        builder.Property(e => e.StoreVersion).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Verdicts).IsRequired().HasMaxLength(32);
        builder.Property(e => e.Answer).HasColumnType("jsonb");
        builder.HasIndex(e => e.ReadAt);
    }
}
