// File: nom-api/Nom.Import/Services/AiServices/AnthropicService.cs

using System.Text;
using System.Text.Json;
using Nom.Import.Services;

namespace Nom.Import.Services.AiServices
{
    public class AnthropicService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public AnthropicService(HttpClient httpClient, string apiKey, string model = "claude-3-sonnet-20240229")
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _model = model;
        }

        public async Task<string> EnhanceIngredientAsync(string prompt)
        {
            return await GetResponseAsync(prompt);
        }

        public async Task<string> GetResponseAsync(string prompt)
        {
            var request = new
            {
                model = _model,
                max_tokens = 1000,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseContent);

            return anthropicResponse?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        }
    }
} 