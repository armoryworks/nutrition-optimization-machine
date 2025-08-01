// File: nom-api/Nom.Import/Services/AiServices/OpenAiService.cs

using System.Text;
using System.Text.Json;
using Nom.Import.Services.Interfaces;

namespace Nom.Import.Services.AiServices
{
    public class OpenAiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenAiService(HttpClient httpClient, string apiKey, string model = "gpt-4")
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _model = model;
        }

        public async Task<string> GetResponseAsync(string prompt)
        {
            var request = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 1000,
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAiResponse = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);

            return openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
    }
} 