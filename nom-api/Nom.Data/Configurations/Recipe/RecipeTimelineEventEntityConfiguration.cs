using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeTimelineEventEntityConfiguration : IEntityTypeConfiguration<RecipeTimelineEventEntity>
{
    public void Configure(EntityTypeBuilder<RecipeTimelineEventEntity> builder)
    {
        builder.ToTable("RecipeTimelineEvent", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.ActorId).IsRequired();
        builder.Property(e => e.EventTypeId).IsRequired();
        builder.Property(e => e.Title).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(2047);
        builder.Property(e => e.Details).HasColumnType("text");

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.TimelineEvents)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventType)
            .WithMany()
            .HasForeignKey(e => e.EventTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
