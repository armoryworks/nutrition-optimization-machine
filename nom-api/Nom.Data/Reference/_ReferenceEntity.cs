using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Abstract base class for all reference items. Implements Table-Per-Hierarchy (TPH).
    /// Maps to the 'Reference.References' table.
    /// </summary>
    [Table("Reference", Schema ="reference")]
    public class ReferenceEntity : BaseEntity // Remains abstract
    {
        [Required]
        public required string Name { get; set; }
        public string? Description { get; set; }

        /// <summary>
        /// Navigation property to a collection of Group entities that this reference belongs to.
        /// This represents the many-to-many relationship between References and Groups.
        /// </summary>
        public virtual ICollection<GroupEntity>? Groups { get; set; } // Implicit many-to-many handled by Fluent API
    }
}