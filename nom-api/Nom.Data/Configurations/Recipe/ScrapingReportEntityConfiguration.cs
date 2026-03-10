using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class ScrapingReportEntityConfiguration : IEntityTypeConfiguration<ScrapingReportEntity>
{
    public void Configure(EntityTypeBuilder<ScrapingReportEntity> builder)
    {
        builder.ToTable("ScrapingReport", schema: "recipe");

        // Properties
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);
        builder.Property(e => e.TotalUrls).IsRequired();
        builder.Property(e => e.SuccessfulScrapes).IsRequired();
        builder.Property(e => e.FailedScrapes).IsRequired();
        builder.Property(e => e.CreatedDate).IsRequired();
        builder.Property(e => e.ErrorDetails).HasColumnType("text");
        builder.Property(e => e.ScrapedUrls).HasColumnType("text");
        builder.Property(e => e.FailedUrls).HasColumnType("text");
    }
}
