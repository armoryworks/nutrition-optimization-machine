using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Plan;

namespace Nom.Data.Configurations.Plan;

public class DietGateBuildEntityConfiguration : IEntityTypeConfiguration<DietGateBuildEntity>
{
    public void Configure(EntityTypeBuilder<DietGateBuildEntity> builder)
    {
        builder.ToTable("DietGateBuild", schema: "plan");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Version).IsRequired().HasMaxLength(255);
        builder.Property(e => e.State).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Note).HasMaxLength(2047);
        builder.HasIndex(e => new { e.State, e.Id });
    }
}

public class DietGateSetEntityConfiguration : IEntityTypeConfiguration<DietGateSetEntity>
{
    public void Configure(EntityTypeBuilder<DietGateSetEntity> builder)
    {
        builder.ToTable("DietGateSet", schema: "plan");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.CriteriaKey).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Criteria).IsRequired();
        builder.Property(e => e.State).IsRequired().HasMaxLength(16);
        builder.HasIndex(e => new { e.BuildId, e.CriteriaKey }).IsUnique();
    }
}

public class DietFoodFactEntityConfiguration : IEntityTypeConfiguration<DietFoodFactEntity>
{
    public void Configure(EntityTypeBuilder<DietFoodFactEntity> builder)
    {
        builder.ToTable("DietFoodFact", schema: "plan");

        builder.HasKey(e => new { e.SetId, e.IngredientId });
        builder.Property(e => e.Verdict).HasConversion<short>();
        builder.Property(e => e.CriterionIds).HasMaxLength(1023);
        builder.Property(e => e.Source).HasMaxLength(16);
        builder.HasIndex(e => e.IngredientId);
    }
}

public class DietFoodStateEntityConfiguration : IEntityTypeConfiguration<DietFoodStateEntity>
{
    public void Configure(EntityTypeBuilder<DietFoodStateEntity> builder)
    {
        builder.ToTable("DietFoodState", schema: "plan");

        builder.HasKey(e => new { e.BuildId, e.IngredientId });
    }
}

public class DietRecipeFlagEntityConfiguration : IEntityTypeConfiguration<DietRecipeFlagEntity>
{
    public void Configure(EntityTypeBuilder<DietRecipeFlagEntity> builder)
    {
        builder.ToTable("DietRecipeFlag", schema: "plan");

        builder.HasKey(e => new { e.SetId, e.RecipeId });
        builder.Property(e => e.Verdict).HasConversion<short>();
        builder.Property(e => e.Reason).HasConversion<short>();
        builder.HasIndex(e => e.RecipeId);
    }
}

public class DietRecipeStateEntityConfiguration : IEntityTypeConfiguration<DietRecipeStateEntity>
{
    public void Configure(EntityTypeBuilder<DietRecipeStateEntity> builder)
    {
        builder.ToTable("DietRecipeState", schema: "plan");

        builder.HasKey(e => new { e.BuildId, e.RecipeId });
    }
}
