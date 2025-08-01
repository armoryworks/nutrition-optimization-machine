// File: Nom.Orch/Models/Household/HouseholdInviteTokenCreateModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Household
{
    public class HouseholdInviteTokenCreateModel
    {
        [Required(ErrorMessage = "Household ID is required.")]
        public long HouseholdId { get; set; }

        [StringLength(255, ErrorMessage = "Token name cannot exceed 255 characters.")]
        public string? Name { get; set; }

        public int? UsesLeft { get; set; }

        public DateTime? ExpirationDate { get; set; }
    }
}