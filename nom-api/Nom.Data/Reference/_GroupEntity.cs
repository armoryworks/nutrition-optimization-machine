using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a category or group for reference items (e.g., "Measurement Units", "Meal Types").
    /// Used exclusively for classifying reference types in the reference namespace.
    /// Maps to the 'reference.Group' table.
    /// </summary>
    [Table("Group", Schema = "reference")]
    public class ReferenceGroupEntity : BaseEntity
    {
        [Required]
        public required string Name { get; set; }

        /// <summary>
        /// Optional description for the group. This property maps to the 'Description' column
        /// in the 'reference.Group' table.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Slug for URL-friendly group identification (from Mealie)
        /// </summary>
        [MaxLength(255)]
        public string? Slug { get; set; }

        /// <summary>
        /// Navigation property to a collection of ReferenceEntity instances
        /// that belong to this group (many-to-many relationship).
        /// </summary>
        public virtual ICollection<ReferenceEntity>? References { get; set; }
    }
}