// File: Nom.Orch/Models/Household/JoinHouseholdRequestModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Household
{
    public class JoinHouseholdRequestModel
    {
        [Required(ErrorMessage = "Invite token is required.")]
        [MinLength(1, ErrorMessage = "Token cannot be empty.")]
        public string Token { get; set; } = string.Empty;
    }
}
