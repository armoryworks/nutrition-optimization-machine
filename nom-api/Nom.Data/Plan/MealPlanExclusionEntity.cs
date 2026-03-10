using System;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a household member's exclusion from a meal or entire day.
    /// MealTypeId = null means excluded for the whole day.
    /// MealTypeId = set means excluded for that specific meal only.
    /// </summary>
    public class MealPlanExclusionEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public DateOnly Date { get; set; }

        public long? MealTypeId { get; set; }
        public virtual ReferenceEntity? MealType { get; set; }
    }
}
