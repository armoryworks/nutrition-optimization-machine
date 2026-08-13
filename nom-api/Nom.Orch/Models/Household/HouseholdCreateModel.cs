// File: Nom.Orch/Models/Household/HouseholdCreateModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Household
{
    public class HouseholdCreateModel
    {
        [Required(ErrorMessage = "Household name is required.")]
        [StringLength(255, ErrorMessage = "Household name cannot exceed 255 characters.")]
        public required string Name { get; set; }

        [StringLength(2047, ErrorMessage = "Description cannot exceed 2047 characters.")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Household group ID is required.")]
        public long HouseholdGroupId { get; set; }

        /// <summary>
        /// True for a solo user's auto-created "personal kitchen" (no member
        /// management, no invites until converted). Set by the onboarding
        /// solo path — not exposed as a user choice on the create screen.
        /// </summary>
        public bool IsPersonal { get; set; }

        // public long AuthorId { get; set; } // REMOVED - Will be set from claims
    }
} 