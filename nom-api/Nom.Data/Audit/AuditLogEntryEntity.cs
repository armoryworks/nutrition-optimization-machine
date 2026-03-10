// Nom.Data/AuditLogEntryEntity.cs
using System;
using Nom.Data.Person; // For ChangedByPerson navigation

namespace Nom.Data.Audit
{
    /// <summary>
    /// Represents an entry in the application's audit log.
    /// This entity tracks changes made to other entities, including who made the change, when,
    /// what entity was affected, and optionally, details about the change (old/new values).
    /// This entity does NOT inherit from BaseEntity to avoid circular auditing.
    /// </summary>
    public class AuditLogEntryEntity
    {
        public long Id { get; set; }

        public required string EntityType { get; set; } // e.g., "Person", "Question", "Recipe"

        public long EntityId { get; set; } // The ID of the entity that was changed

        public required string ChangeType { get; set; } // e.g., "Insert", "Update", "Delete"

        public string? PropertyName { get; set; } // The name of the property that changed (for updates)

        public string? OldValue { get; set; } // Old value of the property (for updates)

        public string? NewValue { get; set; } // New value of the property (for updates)

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public long ChangedByPersonId { get; set; } // ID of the person who made the change

        public virtual PersonEntity ChangedByPerson { get; set; } = default!; // Navigation property to Person
    }
}