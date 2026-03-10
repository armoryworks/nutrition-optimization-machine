using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Recipe;

namespace Nom.Data.Configurations.Recipe;

public class RecipeBulkOperationProgressEntityConfiguration : IEntityTypeConfiguration<RecipeBulkOperationProgressEntity>
{
    public void Configure(EntityTypeBuilder<RecipeBulkOperationProgressEntity> builder)
    {
        builder.ToTable("RecipeBulkOperationProgress", schema: "recipe");

        // Properties
        builder.Property(e => e.OperationId).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Progress).IsRequired();
        builder.Property(e => e.TotalItems).IsRequired();
        builder.Property(e => e.ProcessedItems).IsRequired();
        builder.Property(e => e.SuccessCount).IsRequired();
        builder.Property(e => e.ErrorCount).IsRequired();
        builder.Property(e => e.StartTime).IsRequired();
        builder.Property(e => e.CurrentStep).HasMaxLength(255);
        builder.Property(e => e.ErrorMessages).HasColumnType("text");
        builder.Property(e => e.ProgressDetails).HasColumnType("text");
        builder.Property(e => e.OperationParameters).HasColumnType("text");
    }
}
