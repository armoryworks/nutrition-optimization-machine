using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nom.Data.Person;

namespace Nom.Data.Configurations.Person;

public class PersonAttributeEntityConfiguration : IEntityTypeConfiguration<PersonAttributeEntity>
{
    public void Configure(EntityTypeBuilder<PersonAttributeEntity> builder)
    {
        builder.ToTable("PersonAttribute", schema: "person");

        // Properties
        builder.Property(e => e.PersonId).IsRequired();
        builder.Property(e => e.AttributeTypeId).IsRequired();
        builder.Property(e => e.Value).IsRequired().HasMaxLength(255);
        builder.Property(e => e.OnDate).HasColumnType("date");
    }
}
