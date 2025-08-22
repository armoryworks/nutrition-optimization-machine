// File: nom-api/Nom.Import/Services/AiServices/GoogleGeminiService.cs

using System.Text;
using System.Text.Json;
using Nom.Import.Services;

namespace Nom.Import.Services.AiServices
{
    public class GoogleGeminiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public GoogleGeminiService(HttpClient httpClient, string apiKey, string model = "gemini-pro")
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
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent);

            return geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        }
    }
} 