using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Audit;

namespace Nom.Data.Configurations.Audit;

public class AuditLogEntryEntityConfiguration : IEntityTypeConfiguration<AuditLogEntryEntity>
{
    public void Configure(EntityTypeBuilder<AuditLogEntryEntity> builder)
    {
        builder.ToTable("AuditLogEntry", schema: "audit");

        // AuditLogEntryEntity has its own Key (does NOT inherit BaseEntity)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(256);
        builder.Property(e => e.EntityId).IsRequired();
        builder.Property(e => e.ChangeType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.PropertyName).HasMaxLength(256);
        builder.Property(e => e.OldValue).HasMaxLength(4000);
        builder.Property(e => e.NewValue).HasMaxLength(4000);
        builder.Property(e => e.Timestamp).IsRequired();
        builder.Property(e => e.ChangedByPersonId).IsRequired();

        // Relationships
        builder.HasOne(e => e.ChangedByPerson)
            .WithMany()
            .HasForeignKey(e => e.ChangedByPersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
