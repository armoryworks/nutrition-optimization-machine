// File: Nom.Orch/Models/Household/HouseholdMemberCreateModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Household
{
    public class HouseholdMemberCreateModel
    {
        [Required(ErrorMessage = "Household ID is required.")]
        public long HouseholdId { get; set; }

        [Required(ErrorMessage = "Person ID is required.")]
        public long PersonId { get; set; }
    }
} 