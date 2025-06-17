using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Recipe;

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
        /// Foreign key to the PersonEntity who created/administers this plan.
        /// </summary>
        [Required]
        public long CreatedByPersonId { get; set; }

        /// <summary>
        /// Navigation property to the PersonEntity who created/administers this plan.
        /// </summary>
        [ForeignKey(nameof(CreatedByPersonId))]
        public virtual PersonEntity CreatedByPerson { get; set; } = default!;

        /// <summary>
        /// A unique, nullable code for inviting new participants to this plan.
        /// </summary>
        [MaxLength(50)]
        public string? InvitationCode { get; set; } // NEW: For inviting users

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
