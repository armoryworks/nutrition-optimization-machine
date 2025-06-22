// Nom.Data/IAuditableEntity.cs
using System;

namespace Nom.Data
{
    /// <summary>
    /// Defines common audit properties for entities that track creation and modification.
    /// </summary>
    public interface IAuditableEntity
    {
        DateTime CreatedDate { get; set; }
        long? CreatedByPersonId { get; set; } // Nullable, as 'System' might not have a PersonId or for external imports
        DateTime? LastModifiedDate { get; set; } // Nullable, as not all records will be modified
        long? LastModifiedByPersonId { get; set; } // Nullable
    }
}
