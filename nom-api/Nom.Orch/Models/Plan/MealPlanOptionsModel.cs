using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    /// <summary>Household meal-plan options.</summary>
    public class MealPlanOptionsModel
    {
        /// <summary>Adds a daily Dessert slot to plans and shuffles. Off by default.</summary>
        public bool IncludeDessert { get; set; }

        /// <summary>
        /// The meals (meal type ids: 1100 Breakfast, 1101 Lunch, 1102 Dinner, 1103 Snacks, 1104 Dessert) that the plan
        /// generator fills with a prepared meal by default instead of recipes. Empty by default, so no prepared meal is
        /// generated. On a save, null leaves the stored choice as it is.
        /// </summary>
        public List<long>? PreparedMealTypeIds { get; set; }
    }
}
