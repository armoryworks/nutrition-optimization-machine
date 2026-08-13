namespace Nom.Orch.Models.Plan
{
    /// <summary>
    /// How a household's daily calorie budget divides across meal types.
    /// Percentages must sum to 100. Defaults: 25/30/35/10.
    /// </summary>
    public class MealSplitModel
    {
        public decimal BreakfastPct { get; set; } = 25m;
        public decimal LunchPct { get; set; } = 30m;
        public decimal DinnerPct { get; set; } = 35m;
        public decimal SnacksPct { get; set; } = 10m;

        public decimal Total => BreakfastPct + LunchPct + DinnerPct + SnacksPct;
    }
}
