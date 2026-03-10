using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeStepEntityConfiguration : IEntityTypeConfiguration<RecipeStepEntity>
{
    public void Configure(EntityTypeBuilder<RecipeStepEntity> builder)
    {
        builder.ToTable("RecipeStep", schema: "recipe");

        // Composite key
        builder.HasKey(e => new { e.RecipeId, e.StepNumber });

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.StepNumber).IsRequired();
        builder.Property(e => e.Summary).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(2047);

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.RecipeSteps)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.StepType)
            .WithMany()
            .HasForeignKey(e => e.StepTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
