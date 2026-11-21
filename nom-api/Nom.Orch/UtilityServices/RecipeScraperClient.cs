using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// HTTP client for the operator-provided recipe-scraper service.
    /// Register via AddHttpClient so the underlying handler is pooled.
    /// </summary>
    public class RecipeScraperClient : IRecipeScraperClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly RecipeScraperSettings _settings;
        private readonly ILogger<RecipeScraperClient> _logger;

        public RecipeScraperClient(
            HttpClient httpClient,
            IOptions<RecipeScraperSettings> settings,
            ILogger<RecipeScraperClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;

            if (IsConfigured)
            {
                _httpClient.BaseAddress = new Uri(_settings.BaseUrl!.TrimEnd('/') + "/");
                _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
                _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ApiKey);
            }
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.BaseUrl) && !string.IsNullOrWhiteSpace(_settings.ApiKey);

        public Task<ScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default)
            => PostAsync("api/scrape", new { url }, cancellationToken);

        public Task<ScraperResult> ParseAsync(string content, string? sourceUrl, CancellationToken cancellationToken = default)
            => PostAsync("api/parse", new { content, sourceUrl }, cancellationToken);

        public Task<ScraperJobAnswer> StartDiscoveryJobAsync(IReadOnlyList<string> seedDomains, int maxCandidates, int budgetSeconds, CancellationToken cancellationToken = default)
            => JobAsync(HttpMethod.Post, "api/discover/jobs", new { seedDomains, maxCandidates, budgetSeconds }, cancellationToken);

        public Task<ScraperJobAnswer> StartSourceCheckJobAsync(string domain, int sampleSize, int budgetSeconds, CancellationToken cancellationToken = default)
            => JobAsync(HttpMethod.Post, "api/source-check/jobs", new { domain, sampleSize, budgetSeconds, includeRecipes = true }, cancellationToken);

        public Task<ScraperJobAnswer> GetJobAsync(string kind, string jobId, CancellationToken cancellationToken = default)
            => JobAsync(HttpMethod.Get, $"api/{(kind == "discovery" ? "discover" : "source-check")}/jobs/{Uri.EscapeDataString(jobId)}", null, cancellationToken);

        /// <summary>
        /// One call to a job route. Every way it can go wrong is an answer, never an exception and
        /// never an empty job: the caller counts a lost or failed job as a failed run.
        /// </summary>
        private async Task<ScraperJobAnswer> JobAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                return ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, "No scraper service is configured.");
            }

            try
            {
                using var request = new HttpRequestMessage(method, path);
                if (body != null)
                {
                    request.Content = JsonContent.Create(body, options: JsonOptions);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return ScraperJobAnswer.Fail(ScraperJobOutcome.Busy, "The scraper is running as many jobs of this kind as it allows.");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return ScraperJobAnswer.Fail(ScraperJobOutcome.NotFound, "The scraper has no such job (unknown, expired, or lost in a restart).");
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Scraper job route {Path} returned {StatusCode}", path, response.StatusCode);
                    return ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, $"The scraper returned {(int)response.StatusCode}.");
                }

                var job = await response.Content.ReadFromJsonAsync<ScraperJob>(JsonOptions, cancellationToken);
                return job == null || string.IsNullOrEmpty(job.JobId)
                    ? ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, "The scraper returned no job.")
                    : ScraperJobAnswer.Of(job);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Scraper job route {Path} failed", path);
                return ScraperJobAnswer.Fail(ScraperJobOutcome.Failed, $"Could not reach the scraper: {ex.Message}");
            }
        }

        private async Task<ScraperResult> PostAsync(string path, object body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                return new ScraperResult
                {
                    Success = false,
                    FailureReason = "NotConfigured",
                    Error = "No scraper service is configured. See docs/scraper-integration.md.",
                };
            }

            try
            {
                using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Scraper service returned {StatusCode} for {Path}", response.StatusCode, path);
                    return new ScraperResult
                    {
                        Success = false,
                        FailureReason = "ServiceError",
                        Error = $"Scraper service returned {(int)response.StatusCode}.",
                    };
                }

                var result = await response.Content.ReadFromJsonAsync<ScraperResult>(JsonOptions, cancellationToken);
                return result ?? new ScraperResult
                {
                    Success = false,
                    FailureReason = "ServiceError",
                    Error = "Scraper service returned an empty response.",
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Failed to reach scraper service at {BaseUrl}", _settings.BaseUrl);
                return new ScraperResult
                {
                    Success = false,
                    FailureReason = "ServiceUnreachable",
                    Error = $"Could not reach the scraper service: {ex.Message}",
                };
            }
        }
    }
}
