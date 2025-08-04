namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Scraped ingredient model
    /// </summary>
    public class ScrapedIngredientModel
    {
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string? Notes { get; set; }
    }
} 