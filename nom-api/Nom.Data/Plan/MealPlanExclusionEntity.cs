using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a household member's exclusion from a meal or entire day.
    /// MealTypeId = null means excluded for the whole day.
    /// MealTypeId = set means excluded for that specific meal only.
    /// </summary>
    [Table("MealPlanExclusion", Schema = "plan")]
    public class MealPlanExclusionEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        public long PersonId { get; set; }
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity? Person { get; set; }

        [Required]
        [Column(TypeName = "date")]
        public DateOnly Date { get; set; }

        public long? MealTypeId { get; set; }
        [ForeignKey(nameof(MealTypeId))]
        public virtual ReferenceEntity? MealType { get; set; }
    }
}
