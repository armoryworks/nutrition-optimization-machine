using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// Request model for saving a person's profile (name + attributes).
    /// When personId is 0, creates a new non-user person and optionally adds them to a household.
    /// When personId > 0, updates the existing person. Replaces all existing attributes.
    /// </summary>
    public class SaveProfileRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Email { get; set; }

        /// <summary>
        /// Optional household ID. When creating a new person (id=0), adds them to this household.
        /// The authenticated user must be a member of this household.
        /// </summary>
        public long? HouseholdId { get; set; }

        public List<PersonAttributeRequest> Attributes { get; set; } = new();
    }
}
