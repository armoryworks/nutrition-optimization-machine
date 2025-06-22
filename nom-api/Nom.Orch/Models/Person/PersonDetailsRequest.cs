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
        /// <summary>
        /// This is a generatedId provided by the user interface, it should not be confused with the database Id.
        /// It is used to identify the person in the context of the onboarding process.
        /// It is not the primary key in the database.
        /// </summary>
        public int Id { get; set; }
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        // Add other core person properties as needed for onboarding
        // e.g., public DateTime? DateOfBirth { get; set; }
        // e.g., public string? Gender { get; set; } // Consider using Reference Data for gender
    }
}
