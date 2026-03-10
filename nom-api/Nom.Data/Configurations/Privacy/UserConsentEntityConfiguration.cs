using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Privacy;

namespace Nom.Data.Configurations.Privacy;

public class UserConsentEntityConfiguration : IEntityTypeConfiguration<UserConsentEntity>
{
    public void Configure(EntityTypeBuilder<UserConsentEntity> builder)
    {
        builder.ToTable("UserConsent", schema: "privacy");

        // Properties from BaseEntity
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        // Properties from BasePrivacyEntity
        builder.Property(e => e.PersonId).IsRequired();

        // Own properties
        builder.Property(e => e.ConsentType).IsRequired();

        // Relationships from BasePrivacyEntity
        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId);
    }
}
