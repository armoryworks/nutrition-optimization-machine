// Nom.Orch/UtilityServices/ExternalNutrientApiService.cs
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.Models.NutrientApi; // Now includes FdcSearchResponse and FdcNutrientInfo
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // Required for IConfiguration
using System.Collections.Generic;
using System.Linq;
using System.Net.Http; // Required for HttpClient
using System.Text.Json; // Required for JsonSerializer
using System.Threading.Tasks;
using System;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Service that interacts with the USDA FoodData Central (FDC) API to fetch
    /// food item information and their nutritional values.
    /// This implementation uses HttpClient to make actual HTTP requests.
    /// </summary>
    public class ExternalNutrientApiService : IExternalNutrientApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ExternalNutrientApiService> _logger;
        private readonly string _apiKey;

        public ExternalNutrientApiService(HttpClient httpClient, ILogger<ExternalNutrientApiService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["FoodDataCentralApi:ApiKey"] ??
                      throw new InvalidOperationException("FoodDataCentralApi:ApiKey configuration is missing.");

            // BaseAddress is set via AddHttpClient in Program.cs
        }

        /// <summary>
        /// Searches the FDC database for food items matching a query string.
        /// </summary>
        /// <param name="query">The search term (e.g., "chicken breast", "all-purpose flour").</param>
        /// <param name="limit">Maximum number of results to return.</param>
        /// <returns>A list of matching <see cref="FoodSearchResult"/> objects.</returns>
        public async Task<List<FoodSearchResult>> SearchFoodsAsync(string query, int limit = 5)
        {
            _logger.LogInformation("Searching FDC API for: '{Query}' with limit: {Limit}", query, limit);

            // FDC Search endpoint: /fdc/v1/foods/search?api_key={apiKey}&query={query}&pageSize={pageSize}
            var requestUri = $"foods/search?api_key={_apiKey}&query={Uri.EscapeDataString(query)}&pageSize={limit}";

            try
            {
                var response = await _httpClient.GetAsync(requestUri);
                response.EnsureSuccessStatusCode(); // Throws an HttpRequestException on 4xx or 5xx response

                var jsonResponse = await response.Content.ReadAsStringAsync();
                // Corrected: Deserialize to FdcSearchResponse, then access the Foods property
                var searchResponse = JsonSerializer.Deserialize<FdcSearchResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (searchResponse?.Foods == null)
                {
                    _logger.LogWarning("FDC search for '{Query}' returned no food items in the 'foods' array.", query);
                    return new List<FoodSearchResult>();
                }

                _logger.LogInformation("FDC search for '{Query}' returned {Count} results.", query, searchResponse.Foods.Length);
                return searchResponse.Foods.ToList();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogError("FDC API Rate Limit Exceeded for search query: '{Query}'. Please wait before retrying. Error: {Message}", query, ex.Message);
                // Optionally, implement a retry mechanism or circuit breaker pattern here.
                return new List<FoodSearchResult>(); // Return empty on rate limit
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogError("FDC API Forbidden (API Key Invalid/Missing/Unauthorized) for search query: '{Query}'. Error: {Message}", query, ex.Message);
                // This indicates a critical API key issue.
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
        /// using its FDC ID.
        /// </summary>
        /// <param name="fdcId">The unique ID of the food item in the FDC database.</param>
        /// <returns>A <see cref="FoodDetailResult"/> object containing detailed nutrient data, or null if not found.</returns>
        public async Task<FoodDetailResult?> GetFoodDetailsAsync(string fdcId)
        {
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
                return null; // Return null if food ID not found
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogError("FDC API Rate Limit Exceeded for details FdcId: '{FdcId}'. Error: {Message}", fdcId, ex.Message);
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
