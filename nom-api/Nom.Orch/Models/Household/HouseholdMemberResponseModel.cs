// File: Nom.Orch/Models/Household/HouseholdMemberResponseModel.cs

namespace Nom.Orch.Models.Household
{
    public class HouseholdMemberResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string? PersonEmail { get; set; }
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedDate { get; set; }
        public bool IsActive { get; set; } = true;
        public bool HasProfile { get; set; }
        public bool HasRestrictions { get; set; }

        /// <summary>
        /// True when this member may perform steward actions (member policies,
        /// restriction locks, membership). Mirrors IsAdmin || CanManage.
        /// </summary>
        public bool IsSteward { get; set; }
    }
} 