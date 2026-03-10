namespace Nom.Data.Shopping
{
    public class ShoppingListGenerationHistoryEntity : BaseEntity
    {
        public long ShoppingListId { get; set; }

        public DateTime GeneratedDate { get; set; }

        public string GenerationMethod { get; set; } = string.Empty;

        public int RecipeCount { get; set; }

        public int ItemCount { get; set; }

        public decimal? EstimatedCost { get; set; }

        public bool OptimizationApplied { get; set; }

        public string? OptimizationDetails { get; set; }

        public string? GeneratedItems { get; set; }

        public string? ExcludedItems { get; set; }

        public string? SubstitutionsApplied { get; set; }
    }
}
