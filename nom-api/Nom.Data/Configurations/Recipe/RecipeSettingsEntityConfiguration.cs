using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeSettingsEntityConfiguration : IEntityTypeConfiguration<RecipeSettingsEntity>
{
    public void Configure(EntityTypeBuilder<RecipeSettingsEntity> builder)
    {
        builder.ToTable("RecipeSettings", schema: "recipe");

        // Properties
        builder.Property(e => e.RecipeId).IsRequired();
        builder.Property(e => e.SettingKey).HasMaxLength(255);
        builder.Property(e => e.SettingValue).HasColumnType("text");
        builder.Property(e => e.SettingType).HasMaxLength(255);

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Settings)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
