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


} 