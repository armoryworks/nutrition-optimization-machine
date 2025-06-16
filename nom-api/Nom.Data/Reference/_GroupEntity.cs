using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a category or group for reference items (e.g., "Measurement Units", "Meal Types").
    /// Maps to the 'Reference.Group' table.
    /// </summary>
    [Table("Group", Schema = "reference")]
    public class GroupEntity : BaseEntity
    {
        [Required]
        public required string Name { get; set; }

        /// <summary>
        /// Optional description for the group. This property maps to the 'Description' column
        /// in the 'reference.Group' table.
        /// </summary>
        public string? Description { get; set; } // Ensure this property exists

        /// <summary>
        /// Navigation property to a collection of ReferenceEntity instances
        /// that belong to this group (many-to-many relationship).
        /// </summary>
        public virtual ICollection<ReferenceEntity>? References { get; set; }
    }
}