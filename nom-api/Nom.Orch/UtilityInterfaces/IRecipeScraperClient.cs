using System;
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

        /// <summary>
        /// Starts a budgeted discovery job (POST /api/discover/jobs) that follows outbound links
        /// from the given approved seed domains. Candidates are proposals only — nothing is
        /// imported from them until an admin approves the domain.
        /// </summary>
        Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, CancellationToken cancellationToken = default);

        /// <summary>Starts a source-check job (POST /api/source-check/jobs) for one domain; it returns facts only and imports nothing.</summary>
        Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default);

        /// <summary>Reads a job of either kind (<c>discovery</c>, <c>source-check</c>) by id.</summary>
        Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default);
    }

    /// <summary>What a job route answered: the job, or why there is none.</summary>
    public enum ScraperJobOutcome
    {
        Ok,
        /// <summary>429: as many jobs of the kind are running as the scraper allows.</summary>
        Busy,
        /// <summary>404: the job is unknown, expired or was lost in a restart.</summary>
        NotFound,
        /// <summary>Any other status, a transport error, an unreadable answer or no scraper configured.</summary>
        Failed,
    }

    public sealed record ScraperJobAnswer(ScraperJobOutcome Outcome, ScraperJob? Job, string? Error)
    {
        public static ScraperJobAnswer Of(ScraperJob job) => new(ScraperJobOutcome.Ok, job, null);

        public static ScraperJobAnswer Fail(ScraperJobOutcome outcome, string error) => new(outcome, null, error);
    }

    /// <summary>A scraper job (both kinds share the shape); <see cref="Result"/> is the discovery result or the source-check report.</summary>
    public class ScraperJob
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Partial = "partial";
        public const string Failed = "failed";

        public string JobId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Status { get; set; } = Running;
        public string? StopReason { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset? CreatedUtc { get; set; }
        public DateTimeOffset? DeadlineUtc { get; set; }
        public DateTimeOffset? FinishedUtc { get; set; }
        public int BudgetSeconds { get; set; }
        public double ElapsedSeconds { get; set; }
        public System.Text.Json.JsonElement? Result { get; set; }
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

        [System.Text.Json.Serialization.JsonIgnore]
        public bool PublicDomain { get; set; }
    }

    public class ScraperIngredient
    {
        public string RawLine { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string? Notes { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string? LineKind { get; set; }
    }

    public class ScraperStep
    {
        public int Order { get; set; }
        public string? Section { get; set; }
        public string Instruction { get; set; } = string.Empty;
    }

    public class ScraperDiscoveryResult
    {
        public List<ScraperDiscoveredSource> Candidates { get; set; } = new();
        public int ProbedWithoutSignal { get; set; }
        public bool Partial { get; set; }
        public string? StopReason { get; set; }
        public List<string> SeedsVisited { get; set; } = new();
        public List<string> SeedsNotVisited { get; set; } = new();
        public List<ScraperDiscoveredSource> CandidatesNotProbed { get; set; } = new();
        public List<string> SeedsRefused { get; set; } = new();
        public int PagesFetched { get; set; }
    }

    public class ScraperDiscoveredSource
    {
        public string Domain { get; set; } = string.Empty;
        public string EvidenceUrl { get; set; } = string.Empty;
        public string Signal { get; set; } = string.Empty;
        public string DiscoveredVia { get; set; } = string.Empty;
    }
}
