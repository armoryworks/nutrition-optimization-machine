namespace Nom.Orch.Models.Plan
{
    /// <summary>
    /// Per-member portion breakdown for one planned meal cell. A "plate" is one
    /// serving of every recipe in the cell; members receive plates proportional
    /// to their share of the household's calorie budget for that meal type.
    /// </summary>
    public class PortionBreakdownModel
    {
        public long MealTypeId { get; set; }
        public string MealType { get; set; } = string.Empty;

        /// <summary>Household calories allocated to this meal (daily total × meal split).</summary>
        public decimal BudgetCalories { get; set; }

        /// <summary>Calories in one plate (one serving of each recipe in the cell).</summary>
        public decimal PlateCalories { get; set; }

        /// <summary>Sum of member plates — how many plates to produce.</summary>
        public decimal TotalPlates { get; set; }

        /// <summary>True when no recipe in the cell has calorie data; portions cannot be computed.</summary>
        public bool NoNutritionData { get; set; }

        public List<PortionMemberModel> Members { get; set; } = new();
        public List<PortionRecipeModel> Recipes { get; set; } = new();
    }

    public class PortionMemberModel
    {
        public long PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal TargetCalories { get; set; }
        /// <summary>Where the target came from: "person", "household", or "default".</summary>
        public string TargetSource { get; set; } = "default";
        public decimal SharePct { get; set; }
        public decimal Plates { get; set; }
        public decimal Calories { get; set; }
    }

    public class PortionRecipeModel
    {
        public long RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal PerServingCalories { get; set; }
        public decimal RecipeServings { get; set; }
        /// <summary>Multiply the recipe by this to produce TotalPlates servings.</summary>
        public decimal CookFactor { get; set; }
    }

    /// <summary>One planned recipe's cook factor within a date range (shopping-list scaling).</summary>
    public class RangeCookFactorModel
    {
        public DateOnly Date { get; set; }
        public long MealTypeId { get; set; }
        public long RecipeId { get; set; }
        public decimal CookFactor { get; set; }
    }
}
