using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Communication;

namespace Nom.Data.Configurations.Communication;

public class MessageThreadParticipantEntityConfiguration : IEntityTypeConfiguration<MessageThreadParticipantEntity>
{
    public void Configure(EntityTypeBuilder<MessageThreadParticipantEntity> builder)
    {
        builder.ToTable("MessageThreadParticipant", schema: "communication");

        // Composite key (from Fluent API in OnModelCreating)
        builder.HasKey(e => new { e.MessageThreadId, e.PersonId });

        // Relationships
        builder.HasOne(e => e.MessageThread)
            .WithMany(t => t.Participants)
            .HasForeignKey(e => e.MessageThreadId);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId);
    }
}
