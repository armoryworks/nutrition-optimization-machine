using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class AudienceEntityConfiguration : IEntityTypeConfiguration<AudienceEntity>
{
    public void Configure(EntityTypeBuilder<AudienceEntity> builder)
    {
        builder.ToTable("Audience", schema: "recipe");

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ManagedBy).HasMaxLength(100);

        builder.HasOne(e => e.OwnerPerson)
            .WithMany()
            .HasForeignKey(e => e.OwnerPersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.OwnerPersonId);
        builder.HasIndex(e => e.ManagedBy);
    }
}

public class AudienceMemberEntityConfiguration : IEntityTypeConfiguration<AudienceMemberEntity>
{
    public void Configure(EntityTypeBuilder<AudienceMemberEntity> builder)
    {
        builder.ToTable("AudienceMember", schema: "recipe");

        builder.HasOne(e => e.Audience)
            .WithMany(a => a.Members)
            .HasForeignKey(e => e.AudienceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Household)
            .WithMany()
            .HasForeignKey(e => e.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.AudienceId, e.HouseholdId }).IsUnique();
        builder.HasIndex(e => e.HouseholdId);
    }
}

public class RecipeAudienceEntityConfiguration : IEntityTypeConfiguration<RecipeAudienceEntity>
{
    public void Configure(EntityTypeBuilder<RecipeAudienceEntity> builder)
    {
        builder.ToTable("RecipeAudience", schema: "recipe");

        builder.HasOne(e => e.Recipe)
            .WithMany(r => r.Audiences)
            .HasForeignKey(e => e.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Audience)
            .WithMany(a => a.Recipes)
            .HasForeignKey(e => e.AudienceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.RecipeId, e.AudienceId }).IsUnique();
        builder.HasIndex(e => e.AudienceId);
    }
}
