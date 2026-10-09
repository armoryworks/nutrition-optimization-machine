using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeImageCandidateEntityConfiguration : IEntityTypeConfiguration<RecipeImageCandidateEntity>
{
    public void Configure(EntityTypeBuilder<RecipeImageCandidateEntity> builder)
    {
        builder.ToTable("RecipeImageCandidate", schema: "recipe");

        builder.Property(e => e.Status).IsRequired().HasMaxLength(16);
        builder.Property(e => e.SourceKey).IsRequired().HasMaxLength(32);
        builder.Property(e => e.SourceName).IsRequired().HasMaxLength(64);
        builder.Property(e => e.SourceId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Title).HasMaxLength(511);
        builder.Property(e => e.Author).HasMaxLength(255);
        builder.Property(e => e.AuthorUrl).HasMaxLength(1023);
        builder.Property(e => e.License).IsRequired().HasMaxLength(64);
        builder.Property(e => e.LicenseCode).IsRequired().HasMaxLength(16);
        builder.Property(e => e.LicenseUrl).HasMaxLength(1023);
        builder.Property(e => e.LandingUrl).HasMaxLength(1023);
        builder.Property(e => e.ImageUrl).HasMaxLength(2047);
        builder.Property(e => e.DownloadLocation).HasMaxLength(2047);
        builder.Property(e => e.Score).HasColumnType("decimal(5,4)");
        builder.Property(e => e.Batch).HasMaxLength(128);
        builder.Property(e => e.FilePath).HasMaxLength(1023);
        builder.Property(e => e.ContentType).HasMaxLength(100);

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.RecipeId, e.SourceKey, e.SourceId }).IsUnique();

        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
