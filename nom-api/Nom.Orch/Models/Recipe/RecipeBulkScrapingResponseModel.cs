namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Response model for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingResponseModel
    {
        public long ReportId { get; set; }
        public List<RecipeScrapingResponseModel> Results { get; set; } = new();
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
    }
} 