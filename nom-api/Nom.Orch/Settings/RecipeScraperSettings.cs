namespace Nom.Orch.Settings
{
    /// <summary>
    /// Connection settings for the external recipe-scraper service. NOM ships
    /// without scraping capability; operators run their own scraper service
    /// (any implementation of the contract in docs/scraper-integration.md) and
    /// point these settings at it. When BaseUrl is empty, every scraping
    /// feature is disabled and the API reports scraping as unavailable.
    /// </summary>
    public class RecipeScraperSettings
    {
        public const string SectionName = "RecipeScraper";

        public string? BaseUrl { get; set; }

        /// <summary>Sent as X-Api-Key on every request to the scraper service.</summary>
        public string? ApiKey { get; set; }

        public int TimeoutSeconds { get; set; } = 90;
    }
}
