using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.UtilityServices
{
    public class OllamaClient : IOllamaClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<OllamaClient> _logger;
        private readonly string? _baseUrl;
        private readonly string _model;

        public OllamaClient(HttpClient http, IConfiguration config, ILogger<OllamaClient> logger)
        {
            _http = http;
            _logger = logger;
            _baseUrl = config["Ai:OllamaUrl"]?.TrimEnd('/');
            _model = config["Ai:Model"] ?? "qwen2.5:3b";
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl);

        public async Task<string> GenerateAsync(string prompt, bool jsonMode = false, CancellationToken ct = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Ollama is not configured (set Ai:OllamaUrl).");

            var request = new
            {
                model = _model,
                prompt,
                stream = false,
                format = jsonMode ? "json" : null,
                options = new { temperature = 0 },
            };

            using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/generate", request, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct);
            return body?.Response ?? string.Empty;
        }

        private sealed class OllamaResponse
        {
            [JsonPropertyName("response")]
            public string? Response { get; set; }
        }
    }
}
