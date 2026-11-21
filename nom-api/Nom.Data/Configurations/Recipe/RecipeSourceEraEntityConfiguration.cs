using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeSourceEraEntityConfiguration : IEntityTypeConfiguration<RecipeSourceEraEntity>
{
    public void Configure(EntityTypeBuilder<RecipeSourceEraEntity> builder)
    {
        builder.ToTable("RecipeSourceEra", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.SourceAttribution).IsRequired().HasMaxLength(1023);
        builder.Property(e => e.YearSource).HasMaxLength(255);
        builder.Property(e => e.Era).IsRequired().HasMaxLength(16);

        builder.HasIndex(e => e.SourceAttribution).IsUnique();
    }
}
