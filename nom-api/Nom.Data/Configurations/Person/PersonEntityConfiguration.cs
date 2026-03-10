using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Person;

namespace Nom.Data.Configurations.Person;

public class PersonEntityConfiguration : IEntityTypeConfiguration<PersonEntity>
{
    public void Configure(EntityTypeBuilder<PersonEntity> builder)
    {
        builder.ToTable("Person", schema: "person");

        // Properties
        builder.Property(e => e.Name).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Email).HasMaxLength(255);

        // Indexes
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");

        // Relationships
        builder.HasMany(e => e.PlanParticipations)
            .WithOne(pp => pp.Person)
            .HasForeignKey(pp => pp.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Not mapped
        builder.Ignore(e => e.FavoriteRecipes);
        builder.Ignore(e => e.RatedRecipes);
        builder.Ignore(e => e.RecipeRatings);
        builder.Ignore(e => e.AuthoredRecipes);
    }
}
