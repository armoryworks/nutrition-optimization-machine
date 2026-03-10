using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Curation;

namespace Nom.Data.Configurations.Curation;

public class CurationFeedbackEntityConfiguration : IEntityTypeConfiguration<CurationFeedbackEntity>
{
    public void Configure(EntityTypeBuilder<CurationFeedbackEntity> builder)
    {
        builder.ToTable("CurationFeedback", schema: "curation");

        // Properties from BaseEntity
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Own properties
        builder.Property(e => e.EntityTypeId).IsRequired();
        builder.Property(e => e.AdminId).IsRequired();
        builder.Property(e => e.FeedbackNotes).IsRequired();
        builder.Property(e => e.FeedbackTypeId).IsRequired();

        // Relationships
        builder.HasOne(e => e.Admin)
            .WithMany()
            .HasForeignKey(e => e.AdminId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EntityType)
            .WithMany()
            .HasForeignKey(e => e.EntityTypeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FeedbackType)
            .WithMany()
            .HasForeignKey(e => e.FeedbackTypeId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
