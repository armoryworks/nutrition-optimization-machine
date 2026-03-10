using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Communication;

namespace Nom.Data.Configurations.Communication;

public class MessageThreadEntityConfiguration : IEntityTypeConfiguration<MessageThreadEntity>
{
    public void Configure(EntityTypeBuilder<MessageThreadEntity> builder)
    {
        builder.ToTable("MessageThread", schema: "communication");

        // Properties from BaseEntity
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Relationships
        builder.HasOne(e => e.Recipe)
            .WithMany()
            .HasForeignKey(e => e.RecipeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Ingredient)
            .WithMany()
            .HasForeignKey(e => e.IngredientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Plan)
            .WithMany()
            .HasForeignKey(e => e.PlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
