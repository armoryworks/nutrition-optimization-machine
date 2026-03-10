// File: Nom.Data/Plan/MealPlanRuleEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    public class MealPlanRuleEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long MealTypeId { get; set; }
        public virtual ReferenceEntity? MealType { get; set; }

        public long DayOfWeekId { get; set; }
        public virtual ReferenceEntity? DayOfWeek { get; set; }

        public string? QueryFilter { get; set; }

        public int? MaxRecipes { get; set; }

        public bool IsActive { get; set; } = true;
    }
}