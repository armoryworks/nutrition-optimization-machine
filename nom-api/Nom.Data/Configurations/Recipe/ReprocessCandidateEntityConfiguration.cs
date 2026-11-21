using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class ReprocessCandidateEntityConfiguration : IEntityTypeConfiguration<ReprocessCandidateEntity>
{
    public void Configure(EntityTypeBuilder<ReprocessCandidateEntity> builder)
    {
        builder.ToTable("ReprocessCandidate", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RuleVersion).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Tier).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Outcome).HasMaxLength(16);
        builder.Property(e => e.OutcomeReason).HasMaxLength(255);

        builder.HasIndex(e => e.RecipeId).IsUnique();
        builder.HasIndex(e => new { e.Outcome, e.Priority });
    }
}
