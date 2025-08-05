namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Response model for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingResponseModel
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int TotalUrls { get; set; }
        public int SuccessfulScrapes { get; set; }
        public int FailedScrapes { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public long ReportId { get; set; }
        public List<RecipeScrapingResponseModel> Results { get; set; } = new();
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
    }
} 