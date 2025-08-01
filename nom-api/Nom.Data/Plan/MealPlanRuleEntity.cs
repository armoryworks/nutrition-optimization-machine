// File: Nom.Data/Plan/MealPlanRuleEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    [Table("MealPlanRule", Schema = "plan")]
    public class MealPlanRuleEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        public long MealTypeId { get; set; }
        [ForeignKey(nameof(MealTypeId))]
        public virtual ReferenceEntity? MealType { get; set; }

        [Required]
        public long DayOfWeekId { get; set; }
        [ForeignKey(nameof(DayOfWeekId))]
        public virtual ReferenceEntity? DayOfWeek { get; set; }

        [MaxLength(2047)]
        public string? QueryFilter { get; set; }

        public int? MaxRecipes { get; set; }

        public bool IsActive { get; set; } = true;
    }
}