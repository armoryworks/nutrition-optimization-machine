using System;
using System.Collections.Generic;
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
    /// HTTP client for the operator-provided grocery-export service.
    /// Every failure degrades to an empty/failed result rather than throwing —
    /// a shopping list must never become unusable because a retailer
    /// integration is down.
    /// </summary>
    public class GroceryExportClient : IGroceryExportClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly GroceryExportSettings _settings;
        private readonly ILogger<GroceryExportClient> _logger;

        public GroceryExportClient(
            HttpClient httpClient,
            IOptions<GroceryExportSettings> settings,
            ILogger<GroceryExportClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;

            if (IsConfigured)
            {
                _httpClient.BaseAddress = new Uri(_settings.BaseUrl!.TrimEnd('/') + "/");
                _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
                _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ApiKey);
                if (!string.IsNullOrWhiteSpace(_settings.InstanceId))
                {
                    _httpClient.DefaultRequestHeaders.Add("X-Instance-Id", _settings.InstanceId);
                }
            }
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.BaseUrl) && !string.IsNullOrWhiteSpace(_settings.ApiKey);

        public async Task<List<GroceryProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return new List<GroceryProviderInfo>();
            }

            try
            {
                var providers = await _httpClient.GetFromJsonAsync<List<GroceryProviderInfo>>(
                    "api/providers", JsonOptions, cancellationToken);
                return providers ?? new List<GroceryProviderInfo>();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Grocery service unreachable at {BaseUrl}", _settings.BaseUrl);
                return new List<GroceryProviderInfo>();
            }
        }

        public async Task<GroceryExportResult> ExportAsync(
            GroceryExportRequest request, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return new GroceryExportResult
                {
                    Success = false,
                    Error = "Grocery export is not enabled on this server.",
                };
            }

            try
            {
                using var response = await _httpClient.PostAsJsonAsync("api/export", request, JsonOptions, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Grocery export returned {StatusCode}", response.StatusCode);
                    return new GroceryExportResult
                    {
                        Success = false,
                        Error = $"The grocery service returned {(int)response.StatusCode}.",
                    };
                }

                return await response.Content.ReadFromJsonAsync<GroceryExportResult>(JsonOptions, cancellationToken)
                    ?? new GroceryExportResult { Success = false, Error = "Empty response from the grocery service." };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Grocery export failed");
                return new GroceryExportResult
                {
                    Success = false,
                    Error = $"Could not reach the grocery service: {ex.Message}",
                };
            }
        }

        public async Task<string?> GetAuthorizeUrlAsync(
            string provider, string redirectUri, string state, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return null;
            }

            try
            {
                using var response = await _httpClient.PostAsJsonAsync("api/connect/authorize-url",
                    new { provider, redirectUri, state }, JsonOptions, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return doc.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Grocery authorize-url failed for {Provider}", provider);
                return null;
            }
        }

        public async Task<GroceryTokens?> ExchangeAsync(
            string provider, string? code, string? redirectUri, string? refreshToken,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return null;
            }

            try
            {
                using var response = await _httpClient.PostAsJsonAsync("api/connect/exchange",
                    new { provider, code, redirectUri, refreshToken }, JsonOptions, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Grocery token exchange returned {StatusCode} for {Provider}",
                        response.StatusCode, provider);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<GroceryTokens>(JsonOptions, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Grocery token exchange failed for {Provider}", provider);
                return null;
            }
        }

        public async Task<List<GroceryStore>> FindStoresAsync(
            string provider, string postalCode, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return new List<GroceryStore>();
            }

            try
            {
                var stores = await _httpClient.GetFromJsonAsync<List<GroceryStore>>(
                    $"api/stores?provider={Uri.EscapeDataString(provider)}&postalCode={Uri.EscapeDataString(postalCode)}",
                    JsonOptions, cancellationToken);
                return stores ?? new List<GroceryStore>();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Grocery store lookup failed for {Provider}", provider);
                return new List<GroceryStore>();
            }
        }
    }
}
