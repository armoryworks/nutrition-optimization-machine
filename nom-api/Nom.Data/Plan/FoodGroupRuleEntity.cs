using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    /// <summary>
    /// How a <see cref="FoodGroupRuleEntity"/>'s minimum is counted over time.
    /// </summary>
    public enum FoodGroupRuleTimeframe
    {
        /// <summary>The minimum applies across all of a day's meals combined.</summary>
        PerDay = 1,

        /// <summary>The minimum applies within each individual meal.</summary>
        PerMeal = 2
    }

    /// <summary>
    /// A household requirement that at least <see cref="MinServings"/> servings of a given
    /// nutritional <see cref="FoodGroup"/> appear per day or per meal (<see cref="Timeframe"/>),
    /// optionally scoped to a single <see cref="MealType"/>. Meal-plan generation satisfies these
    /// by counting BOTH standalone food items of the group AND recipes containing ingredients of the
    /// group, topping up with standalone whole foods when recipes fall short. Household-scoped, like
    /// <see cref="MacroGoalEntity"/>. Maps to the 'plan.FoodGroupRule' table.
    /// </summary>
    public class FoodGroupRuleEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>The required food group (reference group FoodGroupType).</summary>
        public long FoodGroupId { get; set; }
        public virtual ReferenceEntity? FoodGroup { get; set; }

        /// <summary>Minimum servings required over the timeframe.</summary>
        public decimal MinServings { get; set; }

        /// <summary>Whether the minimum is counted per day or per individual meal.</summary>
        public FoodGroupRuleTimeframe Timeframe { get; set; } = FoodGroupRuleTimeframe.PerDay;

        /// <summary>
        /// Optional meal-type scope (reference group MealType). NULL = applies to all meals
        /// (for PerDay, counts across every meal; for PerMeal, applies to each meal).
        /// When set, the rule only applies to that meal type.
        /// </summary>
        public long? MealTypeId { get; set; }
        public virtual ReferenceEntity? MealType { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
