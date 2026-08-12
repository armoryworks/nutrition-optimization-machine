using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class ScrapedDocumentEntityConfiguration : IEntityTypeConfiguration<ScrapedDocumentEntity>
{
    public void Configure(EntityTypeBuilder<ScrapedDocumentEntity> builder)
    {
        builder.ToTable("ScrapedDocument", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.SourceUrl).IsRequired().HasMaxLength(2047);
        builder.Property(e => e.RawJsonLd).IsRequired();
        builder.Property(e => e.FetchedAtUtc).IsRequired();

        builder.HasIndex(e => e.RecipeId);

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
