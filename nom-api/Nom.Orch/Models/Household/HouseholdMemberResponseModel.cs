// File: Nom.Orch/Models/Household/HouseholdMemberResponseModel.cs

namespace Nom.Orch.Models.Household
{
    public class HouseholdMemberResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedDate { get; set; }
    }
} 