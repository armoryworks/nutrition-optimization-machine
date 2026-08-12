using System;
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
