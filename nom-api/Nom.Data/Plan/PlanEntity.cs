using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a single meal plan for one or more participants.
    /// Maps to the 'Plan.Plan' table.
    /// </summary>
    [Table("Plan", Schema = "plan")]
    public class PlanEntity : BaseEntity // Inherits Id
    {
        /// <summary>
        /// The name of the plan (e.g., "Family Weekly Plan", "Weight Loss Challenge").
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A description of the plan, its goals, or specific notes.
        /// </summary>
        [MaxLength(2047)]
        public string? Description { get; set; }

        /// <summary>
        /// The start date of the plan.
        /// </summary>
        [Required]
        [Column(TypeName = "date")]
        public DateOnly StartDate { get; set; }

        /// <summary>
        /// The end date of the plan.
        /// </summary>
        [Column(TypeName = "date")]
        public DateOnly? EndDate { get; set; }

        /// <summary>
        /// A unique, nullable code for inviting new participants to this plan.
        /// </summary>
        [MaxLength(50)]
        public string? InvitationCode { get; set; } // NEW: For inviting users

        /// <summary>
        /// Curation status for the plan (NonCurated, PendingCuration, Curated, etc.)
        /// </summary>
        [Required]
        public long CurationStatusId { get; set; } = 9000L; // Default to NonCurated
        [ForeignKey(nameof(CurationStatusId))]
        public virtual ReferenceEntity? CurationStatus { get; set; }

        /// <summary>
        /// The person who created this plan (author)
        /// </summary>
        [Required]
        public long AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        /// <summary>
        /// Date when the plan was submitted for curation
        /// </summary>
        public DateTime? DateSubmittedForCuration { get; set; }

        /// <summary>
        /// Date when curation was completed
        /// </summary>
        public DateTime? DateCurationCompleted { get; set; }

        /// <summary>
        /// Reference to the original curated plan if this is a cloned plan
        /// </summary>
        public long? ParentPlanId { get; set; }
        [ForeignKey(nameof(ParentPlanId))]
        public virtual PlanEntity? ParentPlan { get; set; }

        /// <summary>
        /// Version number for plan versioning
        /// </summary>
        [Required]
        public long Version { get; set; } = 1;

        /// <summary>
        /// Collection of restrictions associated with this plan.
        /// </summary>
        public virtual ICollection<RestrictionEntity> Restrictions { get; set; } = new List<RestrictionEntity>();

        /// <summary>
        /// Collection of meals associated with this plan.
        /// </summary>
        public virtual ICollection<MealEntity> Meals { get; set; } = new List<MealEntity>();

        /// <summary>
        /// Collection of goals associated with this plan.
        /// </summary>
        public virtual ICollection<GoalEntity> Goals { get; set; } = new List<GoalEntity>();

        /// <summary>
        /// Collection of participants in this plan.
        /// </summary>
        public virtual ICollection<PlanParticipantEntity> Participants { get; set; } = new List<PlanParticipantEntity>(); // NEW: Navigation to PlanParticipants
    }
}
