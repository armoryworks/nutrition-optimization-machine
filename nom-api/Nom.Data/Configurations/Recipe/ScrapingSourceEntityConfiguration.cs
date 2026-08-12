using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class ScrapingSourceEntityConfiguration : IEntityTypeConfiguration<ScrapingSourceEntity>
{
    public void Configure(EntityTypeBuilder<ScrapingSourceEntity> builder)
    {
        builder.ToTable("ScrapingSource", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Domain).IsRequired().HasMaxLength(255);
        builder.HasIndex(e => e.Domain).IsUnique();

        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.SampleUrl).HasMaxLength(2047);
        builder.Property(e => e.Notes).HasMaxLength(2047);

        builder.HasOne(e => e.RequestedByPerson)
            .WithMany()
            .HasForeignKey(e => e.RequestedByPersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ReviewedByPerson)
            .WithMany()
            .HasForeignKey(e => e.ReviewedByPersonId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
