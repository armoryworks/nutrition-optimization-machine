using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeNoteEntityConfiguration : IEntityTypeConfiguration<RecipeNoteEntity>
{
    public void Configure(EntityTypeBuilder<RecipeNoteEntity> builder)
    {
        builder.ToTable("RecipeNote", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.AuthorId).IsRequired();
        builder.Property(e => e.Note).IsRequired().HasColumnType("text");
        builder.Property(e => e.Title).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Notes)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
