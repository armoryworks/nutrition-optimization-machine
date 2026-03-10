using System;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a member of a household.
    /// Maps to the 'Plan.household_member' table.
    /// </summary>
    public class HouseholdMemberEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity Household { get; set; } = default!;

        public long PersonId { get; set; }
        public virtual PersonEntity Person { get; set; } = default!;

        public string Role { get; set; } = "Member"; // Member, Admin, etc.

        public DateTime? JoinedDate { get; set; }
        public DateTime? LeftDate { get; set; }
        public bool IsActive { get; set; } = true;

        public bool IsAdmin { get; set; } = false;
        public bool CanManage { get; set; } = false;
        public bool CanInvite { get; set; } = false;
    }
}