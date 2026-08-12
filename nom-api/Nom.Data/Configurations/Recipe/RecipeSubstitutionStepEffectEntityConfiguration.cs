using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeSubstitutionStepEffectEntityConfiguration : IEntityTypeConfiguration<RecipeSubstitutionStepEffectEntity>
{
    public void Configure(EntityTypeBuilder<RecipeSubstitutionStepEffectEntity> builder)
    {
        builder.ToTable("RecipeSubstitutionStepEffect", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.RecipeSubstitutionId).IsRequired();
        builder.Property(e => e.StepNumber).IsRequired();
        builder.Property(e => e.AlteredDescription).IsRequired().HasMaxLength(4095);

        builder.HasIndex(e => new { e.RecipeSubstitutionId, e.StepNumber }).IsUnique();

        builder.HasOne(e => e.RecipeSubstitution)
            .WithMany(s => s.StepEffects)
            .HasForeignKey(e => e.RecipeSubstitutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
