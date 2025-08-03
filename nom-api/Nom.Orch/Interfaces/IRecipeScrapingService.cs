using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Service interface for recipe scraping functionality
    /// </summary>
    public interface IRecipeScrapingService
    {
        /// <summary>
        /// Scrape a recipe from a URL
        /// </summary>
        Task<RecipeScrapingResponseModel> ScrapeRecipeFromUrlAsync(RecipeScrapingRequestModel request);

        /// <summary>
        /// Scrape a recipe from HTML or JSON data
        /// </summary>
        Task<RecipeScrapingResponseModel> ScrapeRecipeFromDataAsync(RecipeScrapingDataRequestModel request);

        /// <summary>
        /// Test recipe scraping from a URL
        /// </summary>
        Task<ScrapedRecipeModel> TestScrapeRecipeAsync(RecipeScrapingTestRequestModel request);

        /// <summary>
        /// Bulk scrape recipes from multiple URLs
        /// </summary>
        Task<RecipeBulkScrapingResponseModel> BulkScrapeRecipesAsync(RecipeBulkScrapingRequestModel request);

        /// <summary>
        /// Get scraping report by ID
        /// </summary>
        Task<RecipeBulkScrapingResponseModel?> GetScrapingReportAsync(long reportId);

        /// <summary>
        /// Get all scraping reports for the current user
        /// </summary>
        Task<List<RecipeBulkScrapingResponseModel>> GetScrapingReportsAsync();
    }
} 