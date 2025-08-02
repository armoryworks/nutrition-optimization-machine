using Nom.Orch.Models.Recipe;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Service for importing recipes from various sources (URLs, images, bulk imports)
    /// Matches Mealie's recipe scraping and import functionality
    /// </summary>
    public interface IRecipeImportOrchestrationService
    {
        /// <summary>
        /// Import a recipe from a URL (web scraping)
        /// </summary>
        Task<RecipeCreateResponseModel> ImportFromUrlAsync(string url, long authorId);

        /// <summary>
        /// Bulk import recipes from multiple URLs
        /// </summary>
        Task<List<RecipeCreateResponseModel>> BulkImportFromUrlsAsync(List<string> urls, long authorId);

        /// <summary>
        /// Import a recipe from an image (OCR processing)
        /// </summary>
        Task<RecipeCreateResponseModel> ImportFromImageAsync(byte[] imageData, long authorId);

        /// <summary>
        /// Import a recipe from HTML or JSON data
        /// </summary>
        Task<RecipeCreateResponseModel> ImportFromHtmlOrJsonAsync(string htmlOrJson, long authorId);

        /// <summary>
        /// Test URL scraping without creating a recipe
        /// </summary>
        Task<RecipeScrapeTestModel> TestUrlScrapingAsync(string url);

        /// <summary>
        /// Import recipes from a ZIP archive
        /// </summary>
        Task<List<RecipeCreateResponseModel>> ImportFromZipAsync(byte[] zipData, long authorId);
    }
} 