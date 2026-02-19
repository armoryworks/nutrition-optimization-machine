using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// Request model for saving a user's profile (name + attributes).
    /// Used by the standalone profile screen. Replaces all existing attributes.
    /// </summary>
    public class SaveProfileRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public List<PersonAttributeRequest> Attributes { get; set; } = new();
    }
}
