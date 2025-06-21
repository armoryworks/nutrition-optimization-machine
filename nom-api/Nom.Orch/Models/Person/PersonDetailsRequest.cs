using System;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// DTO for capturing core person details during onboarding.
    /// Corresponds to PersonEntity.
    /// </summary>
    public class PersonDetailsRequest
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        // Add other core person properties as needed for onboarding
        // e.g., public DateTime? DateOfBirth { get; set; }
        // e.g., public string? Gender { get; set; } // Consider using Reference Data for gender
    }
}
