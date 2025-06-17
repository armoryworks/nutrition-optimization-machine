// Nom.Api/Models/Person/PersonCreateModel.cs
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Models.Person // UPDATED NAMESPACE
{
    /// <summary>
    /// Represents the data required to create a new Person profile.
    /// Assumes the IdentityUser has already been created.
    /// </summary>
    public class PersonCreateModel
    {
        /// <summary>
        /// The ID of the existing IdentityUser to link to this new Person profile.
        /// </summary>
        [Required(ErrorMessage = "Identity User ID is required.")]
        [StringLength(450, ErrorMessage = "Identity User ID cannot exceed 450 characters.")]
        public required string IdentityUserId { get; set; }

        /// <summary>
        /// The name for the new person profile.
        /// </summary>
        [Required(ErrorMessage = "Person name is required.")]
        [StringLength(256, ErrorMessage = "Person name cannot exceed 256 characters.")]
        public required string PersonName { get; set; }
    }
}