using System;
using Nom.Data.Person; // For PersonEntity
using Nom.Data.Reference; // For RoleType (e.g., Admin, Member)

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a participant's association with a specific meal plan.
    /// Maps to the 'Plan.PlanParticipant' table.
    /// </summary>
    public class PlanParticipantEntity : BaseEntity
    {
        public long PlanId { get; set; }

        public virtual PlanEntity Plan { get; set; } = default!;

        public long PersonId { get; set; }

        public virtual PersonEntity Person { get; set; } = default!;

        public long RoleRefId { get; set; } // FK to ReferenceEntity, e.g., "Plan Admin", "Plan Member"

        public virtual ReferenceEntity Role { get; set; } = default!;

        public DateTime JoinedDate { get; set; } = DateTime.UtcNow;


        public bool IsAdmin { get; set; } = false;
        public bool CanManage { get; set; } = false;
        public bool CanInvite { get; set; } = false;
    }
}
