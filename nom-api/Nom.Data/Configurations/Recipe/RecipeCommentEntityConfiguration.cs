using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeCommentEntityConfiguration : IEntityTypeConfiguration<RecipeCommentEntity>
{
    public void Configure(EntityTypeBuilder<RecipeCommentEntity> builder)
    {
        builder.ToTable("RecipeComment", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.AuthorId).IsRequired();
        builder.Property(e => e.Comment).IsRequired().HasColumnType("text");

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Comments)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
