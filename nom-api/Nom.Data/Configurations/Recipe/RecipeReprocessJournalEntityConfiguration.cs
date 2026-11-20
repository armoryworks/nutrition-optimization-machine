using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeReprocessJournalEntityConfiguration : IEntityTypeConfiguration<RecipeReprocessJournalEntity>
{
    public void Configure(EntityTypeBuilder<RecipeReprocessJournalEntity> builder)
    {
        builder.ToTable("RecipeReprocessJournal", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.BatchId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(32);
        builder.Property(e => e.RuleVersion).IsRequired().HasMaxLength(64);
        builder.Property(e => e.OldRows).HasColumnType("jsonb");
        builder.Property(e => e.NewRows).HasColumnType("jsonb");
        builder.Property(e => e.OldRecipe).HasColumnType("jsonb");
        builder.Property(e => e.NewRecipe).HasColumnType("jsonb");

        builder.HasIndex(e => e.RecipeId);
        builder.HasIndex(e => e.BatchId);
        builder.HasIndex(e => e.CreatedDate);
    }
}
