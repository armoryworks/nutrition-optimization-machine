// Nom.Data/AuditLogEntryEntity.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person; // For ChangedByPerson navigation

namespace Nom.Data
{
    /// <summary>
    /// Represents an entry in the application's audit log.
    /// This entity tracks changes made to other entities, including who made the change, when,
    /// what entity was affected, and optionally, details about the change (old/new values).
    /// This entity does NOT inherit from BaseEntity to avoid circular auditing.
    /// </summary>
    [Table("AuditLogEntry", Schema = "audit")] // Define a specific schema for audit logs
    public class AuditLogEntryEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(256)]
        public required string EntityType { get; set; } // e.g., "Person", "Question", "Recipe"

        [Required]
        public long EntityId { get; set; } // The ID of the entity that was changed

        [Required]
        [MaxLength(50)]
        public required string ChangeType { get; set; } // e.g., "Insert", "Update", "Delete"

        [MaxLength(256)]
        public string? PropertyName { get; set; } // The name of the property that changed (for updates)

        [MaxLength(4000)]
        public string? OldValue { get; set; } // Old value of the property (for updates)

        [MaxLength(4000)]
        public string? NewValue { get; set; } // New value of the property (for updates)

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        public long ChangedByPersonId { get; set; } // ID of the person who made the change

        [ForeignKey(nameof(ChangedByPersonId))]
        public virtual PersonEntity ChangedByPerson { get; set; } = default!; // Navigation property to Person
    }
}