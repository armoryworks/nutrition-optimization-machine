using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Client for the external recipe-scraper service (contract:
    /// docs/scraper-integration.md). The service is operator-provided and
    /// optional — check <see cref="IsConfigured"/> before calling.
    /// </summary>
    public interface IRecipeScraperClient
    {
        bool IsConfigured { get; }

        /// <summary>Fetch a URL and extract its recipe. The service enforces robots.txt and rate limits.</summary>
        Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default);

        /// <summary>Parse caller-supplied HTML or JSON-LD without fetching anything.</summary>
        Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default);
    }

    // DTOs mirroring the scraper service's response contract.

    public class ScraperResult
    {
        public bool Success { get; set; }

        public ScraperRecipe? Recipe { get; set; }

        /// <summary>
        /// One of: None, InvalidUrl, DomainNotAllowed, RobotsDisallowed,
        /// FetchFailed, NotHtml, ResponseTooLarge, NoStructuredRecipeData.
        /// </summary>
        public string? FailureReason { get; set; }

        public string? Error { get; set; }
    }

    public class ScraperRecipe
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Author { get; set; }
        public string? SourceUrl { get; set; }
        public string? SourceSite { get; set; }
        public string? PrepTime { get; set; }
        public string? CookTime { get; set; }
        public string? TotalTime { get; set; }
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public int? TotalTimeMinutes { get; set; }
        public string? RecipeYield { get; set; }
        public decimal? RecipeServings { get; set; }
        public List<ScraperIngredient> Ingredients { get; set; } = new();
        public List<ScraperStep> Steps { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        public List<string> Categories { get; set; } = new();
        public List<string> Cuisines { get; set; } = new();
        public List<string> SuitableForDiet { get; set; } = new();
        public string? RawJsonLd { get; set; }
    }

    public class ScraperIngredient
    {
        public string RawLine { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string? Notes { get; set; }
    }

    public class ScraperStep
    {
        public int Order { get; set; }
        public string? Section { get; set; }
        public string Instruction { get; set; } = string.Empty;
    }
}
