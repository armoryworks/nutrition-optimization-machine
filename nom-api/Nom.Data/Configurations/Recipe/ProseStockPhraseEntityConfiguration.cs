using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class ProseStockPhraseEntityConfiguration : IEntityTypeConfiguration<ProseStockPhraseEntity>
{
    public void Configure(EntityTypeBuilder<ProseStockPhraseEntity> builder)
    {
        builder.ToTable("ProseStockPhrase", schema: "recipe");

        builder.HasKey(e => new { e.Version, e.Hash });
        builder.Property(e => e.Hash).ValueGeneratedNever();
        builder.Property(e => e.SiteCount).IsRequired();
        builder.Property(e => e.Version).IsRequired();
    }
}
