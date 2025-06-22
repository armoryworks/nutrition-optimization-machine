using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person; // For PersonEntity
using Nom.Data.Reference; // For RoleType (e.g., Admin, Member)

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a participant's association with a specific meal plan.
    /// Maps to the 'Plan.PlanParticipant' table.
    /// </summary>
    [Table("PlanParticipant", Schema = "plan")]
    public class PlanParticipantEntity : BaseEntity
    {
        [Required]
        public long PlanId { get; set; }

        [ForeignKey(nameof(PlanId))]
        public virtual PlanEntity Plan { get; set; } = default!;

        [Required]
        public long PersonId { get; set; }

        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity Person { get; set; } = default!;

        [Required]
        public long RoleRefId { get; set; } // FK to ReferenceEntity, e.g., "Plan Admin", "Plan Member"

        [ForeignKey(nameof(RoleRefId))]
        public virtual ReferenceEntity Role { get; set; } = default!;

        [Required]
        public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
    }
}
