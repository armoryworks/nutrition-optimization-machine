namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Web Scraping service
    /// </summary>
    public interface IWebScrapingService
    {
        /// <summary>
        /// Scrapes recipe data from a URL
        /// </summary>
        /// <param name="url">The URL to scrape</param>
        /// <returns>Scraped recipe data</returns>
        Task<ScrapedRecipeData> ScrapeRecipeFromUrlAsync(string url);
    }

    /// <summary>
    /// Scraped recipe data structure
    /// </summary>
    public class ScrapedRecipeData
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public List<string> Ingredients { get; set; } = new List<string>();
        public List<string> Instructions { get; set; } = new List<string>();
        public string PrepTime { get; set; } = string.Empty;
        public string CookTime { get; set; } = string.Empty;
        public string TotalTime { get; set; } = string.Empty;
        public string Yield { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
} 