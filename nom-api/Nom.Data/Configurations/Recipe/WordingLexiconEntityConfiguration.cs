using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class WordingLexiconEntityConfiguration : IEntityTypeConfiguration<WordingLexiconEntity>
{
    public void Configure(EntityTypeBuilder<WordingLexiconEntity> builder)
    {
        builder.ToTable("WordingLexicon", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Term).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Means).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Source).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Batch).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Note).HasMaxLength(1023);

        builder.HasIndex(e => e.Term);
        builder.HasIndex(e => e.Batch);
    }
}
