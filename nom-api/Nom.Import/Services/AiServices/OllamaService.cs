// File: nom-api/Nom.Import/Services/AiServices/OllamaService.cs

using System.Text;
using System.Text.Json;
using Nom.Import.Services.Interfaces;

namespace Nom.Import.Services.AiServices
{
    public class OllamaService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;

        public OllamaService(HttpClient httpClient, string model = "llama2", string baseUrl = "http://localhost:11434")
        {
            _httpClient = httpClient;
            _model = model;
            _baseUrl = baseUrl;
        }

        public async Task<string> GetResponseAsync(string prompt)
        {
            var request = new
            {
                model = _model,
                prompt = prompt,
                stream = false
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent);

            return ollamaResponse?.Response ?? string.Empty;
        }
    }
} 