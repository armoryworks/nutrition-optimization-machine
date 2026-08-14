using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class DishGroupEntityConfiguration : IEntityTypeConfiguration<DishGroupEntity>
{
    public void Configure(EntityTypeBuilder<DishGroupEntity> builder)
    {
        builder.ToTable("DishGroup", schema: "recipe");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.Slug).IsUnique();

        builder.HasMany(e => e.Recipes)
            .WithOne(r => r.DishGroup)
            .HasForeignKey(r => r.DishGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
