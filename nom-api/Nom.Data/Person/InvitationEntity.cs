using System;
using Nom.Data.Audit;

namespace Nom.Data.Person
{
    /// <summary>
    /// Represents an invitation to join the system or a specific plan.
    /// Handles invitation codes separately from Person entities for better tracking and flexibility.
    /// </summary>
    public class InvitationEntity : BaseExpirationEntity
    {
        /// <summary>
        /// The unique invitation code that can be shared with invitees.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// The person who created this invitation (inviter).
        /// </summary>
        public long InviterPersonId { get; set; }
        public virtual PersonEntity Inviter { get; set; } = default!;

        /// <summary>
        /// The person who was invited (invitee). Null until the invitation is claimed.
        /// </summary>
        public long? InviteePersonId { get; set; }
        public virtual PersonEntity? Invitee { get; set; }

        /// <summary>
        /// Whether the invitation has been used/claimed.
        /// </summary>
        public bool IsUsed { get; set; } = false;

        /// <summary>
        /// When the invitation was used/claimed.
        /// </summary>
        public DateTime? UsedAt { get; set; }

        /// <summary>
        /// Optional notes about the invitation.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// The type of invitation (e.g., "Plan Invitation", "System Invitation").
        /// </summary>
        public string InvitationType { get; set; } = string.Empty;

        /// <summary>
        /// Optional reference to a specific plan if this is a plan invitation.
        /// </summary>
        public long? PlanId { get; set; }
        public virtual Plan.PlanEntity? Plan { get; set; }
    }
}