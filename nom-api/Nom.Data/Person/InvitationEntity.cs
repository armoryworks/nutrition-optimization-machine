using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Person
{
    /// <summary>
    /// Represents an invitation to join the system or a specific plan.
    /// Handles invitation codes separately from Person entities for better tracking and flexibility.
    /// </summary>
    [Table("Invitation", Schema = "person")]
    public class InvitationEntity : BaseEntity
    {
        /// <summary>
        /// The unique invitation code that can be shared with invitees.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// The person who created this invitation (inviter).
        /// </summary>
        [Required]
        public long InviterPersonId { get; set; }
        [ForeignKey(nameof(InviterPersonId))]
        public virtual PersonEntity Inviter { get; set; } = default!;

        /// <summary>
        /// The person who was invited (invitee). Null until the invitation is claimed.
        /// </summary>
        public long? InviteePersonId { get; set; }
        [ForeignKey(nameof(InviteePersonId))]
        public virtual PersonEntity? Invitee { get; set; }

        /// <summary>
        /// When the invitation expires. Null means no expiration.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

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
        [MaxLength(2047)]
        public string? Notes { get; set; }

        /// <summary>
        /// The type of invitation (e.g., "Plan Invitation", "System Invitation").
        /// </summary>
        [MaxLength(255)]
        public string InvitationType { get; set; } = string.Empty;

        /// <summary>
        /// Optional reference to a specific plan if this is a plan invitation.
        /// </summary>
        public long? PlanId { get; set; }
        [ForeignKey(nameof(PlanId))]
        public virtual Plan.PlanEntity? Plan { get; set; }
    }
} 