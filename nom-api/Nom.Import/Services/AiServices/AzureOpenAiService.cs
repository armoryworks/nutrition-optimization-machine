// File: nom-api/Nom.Import/Services/AiServices/AzureOpenAiService.cs

using System.Text;
using System.Text.Json;
using Nom.Import.Services.Interfaces;

namespace Nom.Import.Services.AiServices
{
    public class AzureOpenAiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _deploymentName;

        public AzureOpenAiService(HttpClient httpClient, string apiKey, string endpoint, string deploymentName)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _endpoint = endpoint;
            _deploymentName = deploymentName;
        }

        public async Task<string> GetResponseAsync(string prompt)
        {
            var request = new
            {
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 1000,
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

            var response = await _httpClient.PostAsync($"{_endpoint}/openai/deployments/{_deploymentName}/chat/completions?api-version=2023-05-15", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var openAiResponse = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);

            return openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
    }
} 