using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Shopping; // For PantryItemEntity relationship

namespace Nom.Data.Plan
{
    [Table("Plan", Schema = "plan")]
    public class PlanEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [MaxLength(2047)]
        public string? Purpose { get; set; }

        [Column(TypeName = "date")]
        public DateOnly? BeginDate { get; set; }

        [Column(TypeName = "date")]
        public DateOnly? EndDate { get; set; }

        public virtual ICollection<MealEntity>? Meals { get; set; }
        public virtual ICollection<GoalEntity>? Goals { get; set; }
        public virtual ICollection<RestrictionEntity>? Restrictions { get; set; }

        public virtual ICollection<PersonEntity>? Participants { get; set; }
        public virtual ICollection<PersonEntity>? Administrators { get; set; }

        // --- Navigation Property for Pantry Items ---

        /// <summary>
        /// Navigation property to a collection of PantryItem entities associated with this plan.
        /// This represents the estimated inventory (pantry and items on shopping lists) for this plan.
        /// </summary>
        public virtual ICollection<Shopping.PantryItemEntity>? PantryItems { get; set; } // Type is PantryItemEntity

        // --- END NEW ---
    }
}