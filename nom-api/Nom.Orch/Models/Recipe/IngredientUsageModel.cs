namespace Nom.Orch.Models.Recipe
{
    public class IngredientUsageModel
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int UsageCount { get; set; }
    }
}
