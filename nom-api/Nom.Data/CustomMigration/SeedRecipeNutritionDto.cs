namespace Nom.Data.CustomMigration
{
    internal class SeedRecipeNutritionDto
    {
        public long NutrientId { get; set; }
        public decimal Amount { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal? DailyValuePercentage { get; set; }
    }
}
