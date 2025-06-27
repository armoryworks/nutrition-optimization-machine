// Nom.Orch/UtilityServices/ExternalNutrientApiService.cs
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.Models.NutrientApi; // For FoodSearchResult, FoodDetailResult, FdcSearchResponse
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // Required for IConfiguration
using System.Collections.Generic;
using System.Linq;
using System.Net.Http; // Required for HttpClient
using System.Text.Json; // Required for JsonSerializer
using System.Threading.Tasks;
using System;
using System.Web; // Required for HttpUtility.ParseQueryString
using System.Threading; // Required for SemaphoreSlim

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Service that interacts with the USDA FoodData Central (FDC) API to fetch
    /// food item information and their nutritional values.
    /// This implementation includes rate limiting to respect external API constraints (1000 requests/hour)
    /// and supports filtering search results by FDC data types.
    /// </summary>
    public class ExternalNutrientApiService : IExternalNutrientApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ExternalNutrientApiService> _logger;
        private readonly string _apiKey;

        // --- Rate Limiting Configuration ---
        private const int MaxRequestsPerHour = 950; // Slightly less than 1000 to be safe
        private const int RateLimitWindowMinutes = 60; // 1 hour
        private static int _requestCount = 0;
        private static DateTime _lastResetTime = DateTime.UtcNow;
        private static readonly SemaphoreSlim _rateLimitSemaphore = new SemaphoreSlim(1, 1); // Ensures only one thread modifies counters at a time

        private const string BaseSearchUrl = "fdc/v1/foods/search";
        private const string BaseDetailsUrl = "fdc/v1/food/";

        public ExternalNutrientApiService(HttpClient httpClient, ILogger<ExternalNutrientApiService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["FoodDataCentralApi:ApiKey"] ??
                      throw new InvalidOperationException("FoodDataCentralApi:ApiKey configuration is missing.");

            // BaseAddress is set via AddHttpClient in Program.cs
        }

        /// <summary>
        /// Waits if necessary to ensure the API rate limit is not exceeded.
        /// This method is designed to be thread-safe and to block until a request can proceed.
        /// </summary>
        private async Task WaitForRateLimitAllowance()
        {
            await _rateLimitSemaphore.WaitAsync(); // Acquire lock
            try
            {
                var now = DateTime.UtcNow;

                // If a new hour has started, reset the counter
                if ((now - _lastResetTime).TotalMinutes >= RateLimitWindowMinutes)
                {
                    _requestCount = 0;
                    _lastResetTime = now;
                    _logger.LogInformation("FDC API rate limit counter reset for new hour.");
                }

                // If the limit is reached, wait until the next hour
                if (_requestCount >= MaxRequestsPerHour)
                {
                    var timeToWait = (TimeSpan.FromMinutes(RateLimitWindowMinutes) - (now - _lastResetTime));
                    _logger.LogWarning("FDC API rate limit ({MaxRequests}/{WindowMin}min) exceeded. Waiting for {TimeToWait} before next request.",
                        MaxRequestsPerHour, RateLimitWindowMinutes, timeToWait);
                    await Task.Delay(timeToWait); // Wait for the remainder of the hour

                    // After waiting, reset the counter for the *new* hour
                    _requestCount = 0;
                    _lastResetTime = DateTime.UtcNow;
                }

                _requestCount++; // Increment for the current request
                _logger.LogDebug("FDC API request count: {Count}/{Max}", _requestCount, MaxRequestsPerHour);
            }
            finally
            {
                _rateLimitSemaphore.Release(); // Release lock
            }
        }


        /// <summary>
        /// Searches the FDC database for food items matching a query string.
        /// Includes rate limiting and supports filtering by FDC data types.
        /// </summary>
        /// <param name="query">The search term (e.g., "chicken breast", "all-purpose flour").</param>
        /// <param name="limit">Maximum number of results to return.</param>
        /// <param name="dataTypes">Optional. A list of FDC data types to filter results (e.g., "Branded", "SR Legacy").</param>
        /// <returns>A list of matching <see cref="FoodSearchResult"/> objects.</returns>
        public async Task<List<FoodSearchResult>> SearchFoodsAsync(string query, int limit = 5, List<string>? dataTypes = null)
        {
            await WaitForRateLimitAllowance(); // Apply rate limit before making the actual API call

            try
            {
                var uriBuilder = new UriBuilder(_httpClient.BaseAddress + BaseSearchUrl);
                var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query); // Use HttpUtility.ParseQueryString to manage parameters

                queryParams["query"] = query;
                queryParams["api_key"] = _apiKey;
                queryParams["pageSize"] = limit.ToString();

                if (dataTypes != null && dataTypes.Any())
                {
                    // FDC API supports multiple data types by repeating the parameter, e.g., &dataType=Branded&dataType=SR Legacy
                    foreach (var type in dataTypes)
                    {
                        queryParams.Add("dataType", type);
                    }
                }

                uriBuilder.Query = queryParams.ToString(); // Set the updated query string
                var requestUri = uriBuilder.ToString();

                _logger.LogInformation("Calling FDC Search API: {RequestUri}", requestUri);

                var response = await _httpClient.GetAsync(requestUri);
                response.EnsureSuccessStatusCode(); // Throws an HttpRequestException on 4xx or 5xx response

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var searchResponse = JsonSerializer.Deserialize<FdcSearchResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (searchResponse?.Foods == null)
                {
                    _logger.LogWarning("FDC search for '{Query}' returned no food items in the 'foods' array.", query);
                    return new List<FoodSearchResult>();
                }

                _logger.LogInformation("FDC search for '{Query}' returned {Count} results.", query, searchResponse.Foods.Count);
                return searchResponse.Foods.ToList();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogError("FDC API Rate Limit Exceeded (server-side) for search query: '{Query}'. Please wait before retrying. Error: {Message}", query, ex.Message);
                return new List<FoodSearchResult>();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogError("FDC API Forbidden (API Key Invalid/Missing/Unauthorized) for search query: '{Query}'. Error: {Message}", query, ex.Message);
                return new List<FoodSearchResult>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed during FDC search for '{Query}': {Message}", query, ex.Message);
                return new List<FoodSearchResult>();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize FDC search response for '{Query}': {Message}", query, ex.Message);
                return new List<FoodSearchResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during FDC search for '{Query}': {Message}", query, ex.Message);
                return new List<FoodSearchResult>();
            }
        }

        /// <summary>
        /// Retrieves detailed nutrient information for a specific food item
        /// using its FDC ID. Includes rate limiting.
        /// </summary>
        /// <param name="fdcId">The unique ID of the food item in the FDC database.</param>
        /// <returns>A <see cref="FoodDetailResult"/> object containing detailed nutrient data, or null if not found.</returns>
        public async Task<FoodDetailResult?> GetFoodDetailsAsync(string fdcId)
        {
            await WaitForRateLimitAllowance(); // Apply rate limit before making the actual API call

            _logger.LogInformation("Fetching FDC API details for FdcId: '{FdcId}'", fdcId);

            // FDC Detail endpoint: /fdc/v1/food/{fdcId}?api_key={apiKey}
            var requestUri = $"food/{fdcId}?api_key={_apiKey}";

            try
            {
                var response = await _httpClient.GetAsync(requestUri);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var foodDetail = JsonSerializer.Deserialize<FoodDetailResult>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                _logger.LogInformation("FDC detail fetch for '{FdcId}' successful.", fdcId);
                return foodDetail;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("FDC API Food details not found for FdcId: '{FdcId}'. Error: {Message}", fdcId, ex.Message);
                return null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogError("FDC API Rate Limit Exceeded (server-side) for details FdcId: '{FdcId}'. Error: {Message}", fdcId, ex.Message);
                return null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogError("FDC API Forbidden (API Key Invalid/Missing/Unauthorized) for FdcId: '{FdcId}'. Error: {Message}", fdcId, ex.Message);
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed during FDC detail fetch for FdcId: '{FdcId}': {Message}", fdcId, ex.Message);
                return null;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize FDC detail response for FdcId: '{FdcId}': {Message}", fdcId, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during FDC detail fetch for FdcId: '{FdcId}': {Message}", fdcId, ex.Message);
                return null;
            }
        }
    }
}
