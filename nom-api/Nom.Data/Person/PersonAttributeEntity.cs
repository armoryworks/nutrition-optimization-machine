using System; // Required for DateOnly or DateTime
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // Required for AttributeType navigation property

namespace Nom.Data.Person
{
    /// <summary>
    /// Represents an attribute associated with a specific person (e.g., height, weight, dietary preference).
    /// Maps to the 'Person.person_attribute' table.
    /// </summary>
    [Table("PersonAttribute", Schema = "person")] // Table name capitalized, schema lowercase
    public class PersonAttributeEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Person.person table. Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long PersonId { get; set; }

        /// <summary>
        /// Navigation property to the associated PersonEntity.
        /// </summary>
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity Person { get; set; } = default!; // Required navigation property

        /// <summary>
        /// Foreign key to the Reference.reference table, indicating the type of attribute
        /// (e.g., "Height", "Weight", "Goal"). Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long AttributeTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the attribute type.
        /// </summary>
        [ForeignKey(nameof(AttributeTypeId))]
        public virtual ReferenceEntity AttributeType { get; set; } = default!; // Required navigation property

        /// <summary>
        /// The value of the attribute. Corresponds to VARCHAR(255) NOT NULL.
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Value { get; set; } = string.Empty; // Required string property, initialized to prevent null warnings

        /// <summary>
        /// The date on which the attribute value was recorded. Corresponds to DATE NULL.
        /// </summary>
        [Column(TypeName = "date")] // Explicitly maps to SQL DATE type
        public DateOnly? OnDate { get; set; } // Using DateOnly for DATE type, or DateTime? if targeting older .NET versions/preferences
    }
}