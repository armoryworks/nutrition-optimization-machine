// File: Nom.Orch/Models/Household/HouseholdInviteTokenResponseModel.cs

namespace Nom.Orch.Models.Household
{
    public class HouseholdInviteTokenResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
} 