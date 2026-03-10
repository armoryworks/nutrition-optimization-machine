using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Communication;

namespace Nom.Data.Configurations.Communication;

public class MessageEntityConfiguration : IEntityTypeConfiguration<MessageEntity>
{
    public void Configure(EntityTypeBuilder<MessageEntity> builder)
    {
        builder.ToTable("Message", schema: "communication");

        // Properties from BaseEntity
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Relationships
        builder.HasOne(e => e.MessageThread)
            .WithMany(t => t.Messages)
            .HasForeignKey(e => e.MessageThreadId);

        builder.HasOne(e => e.SenderPerson)
            .WithMany()
            .HasForeignKey(e => e.SenderPersonId);
    }
}
