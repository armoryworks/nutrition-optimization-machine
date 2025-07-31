using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Nom.Import.Services
{
    /// <summary>
    /// Interface for AI service implementations.
    /// </summary>
    public interface IAiService
    {
        Task<string> ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// OpenAI GPT implementation.
    /// Best for: High quality, detailed responses, good at following instructions.
    /// Cost: ~$0.03 per 1K tokens (GPT-4)
    /// Speed: Moderate
    /// </summary>
    public class OpenAiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OpenAiService> _logger;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenAiService(HttpClient httpClient, ILogger<OpenAiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            _model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4";
        }

        public async Task<string> ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("OpenAI API key not configured");
            }

            var request = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant that enhances ingredient data for a nutrition application. Always respond with valid JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"OpenAI API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);
            
            return responseObj?.choices?.FirstOrDefault()?.message?.content ?? "";
        }
    }

    /// <summary>
    /// Anthropic Claude implementation.
    /// Best for: High quality, detailed responses, excellent at following complex instructions.
    /// Cost: ~$0.015 per 1K tokens (Claude-3.5 Sonnet)
    /// Speed: Moderate to fast
    /// </summary>
    public class AnthropicService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AnthropicService> _logger;
        private readonly string _apiKey;
        private readonly string _model;

        public AnthropicService(HttpClient httpClient, ILogger<AnthropicService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
            _model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-3-5-sonnet-20241022";
        }

        public async Task<string> ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Anthropic API key not configured");
            }

            var request = new
            {
                model = _model,
                max_tokens = 500,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Anthropic API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<AnthropicResponse>(responseContent);
            
            return responseObj?.content?.FirstOrDefault()?.text ?? "";
        }
    }

    /// <summary>
    /// Google Gemini implementation.
    /// Best for: Good quality, cost-effective, good at structured tasks.
    /// Cost: ~$0.0005 per 1K tokens (Gemini Pro)
    /// Speed: Fast
    /// </summary>
    public class GoogleGeminiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GoogleGeminiService> _logger;
        private readonly string _apiKey;
        private readonly string _model;

        public GoogleGeminiService(HttpClient httpClient, ILogger<GoogleGeminiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ?? "";
            _model = Environment.GetEnvironmentVariable("GOOGLE_MODEL") ?? "gemini-1.5-pro";
        }

        public async Task<string> ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Google API key not configured");
            }

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
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 500
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Google Gemini API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
            
            return responseObj?.candidates?.FirstOrDefault()?.content?.parts?.FirstOrDefault()?.text ?? "";
        }
    }

    /// <summary>
    /// Azure OpenAI implementation (Microsoft's hosted OpenAI).
    /// Best for: Enterprise environments, good integration with Microsoft ecosystem.
    /// Cost: Similar to OpenAI, but with enterprise features
    /// Speed: Moderate
    /// </summary>
    public class AzureOpenAiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AzureOpenAiService> _logger;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _deploymentName;

        public AzureOpenAiService(HttpClient httpClient, ILogger<AzureOpenAiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? "";
            _endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";
            _deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "";
        }

        public async Task<string> ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_endpoint) || string.IsNullOrEmpty(_deploymentName))
            {
                throw new InvalidOperationException("Azure OpenAI configuration incomplete");
            }

            var request = new
            {
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant that enhances ingredient data for a nutrition application. Always respond with valid JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

            var url = $"{_endpoint}/openai/deployments/{_deploymentName}/chat/completions?api-version=2024-02-15-preview";
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Azure OpenAI API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);
            
            return responseObj?.choices?.FirstOrDefault()?.message?.content ?? "";
        }
    }

    /// <summary>
    /// Local/Offline AI implementation using Ollama.
    /// Best for: Privacy, no API costs, offline processing.
    /// Cost: Free (except compute)
    /// Speed: Depends on local hardware
    /// </summary>
    public class OllamaService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OllamaService> _logger;
        private readonly string _model;
        private readonly string _baseUrl;

        public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama2:7b";
            _baseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
        }

        public async Task<string> ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            var request = new
            {
                model = _model,
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.2,  // Lower temperature for more consistent JSON
                    num_predict = 300   // Shorter response to prevent verbose output
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Ollama API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<OllamaResponse>(responseContent);
            
            return responseObj?.response ?? "";
        }
    }

    #region Response Models

    public class OpenAiResponse
    {
        public List<OpenAiChoice> choices { get; set; } = new List<OpenAiChoice>();
    }

    public class OpenAiChoice
    {
        public OpenAiMessage message { get; set; } = new OpenAiMessage();
    }

    public class OpenAiMessage
    {
        public string content { get; set; } = string.Empty;
    }

    public class AnthropicResponse
    {
        public List<AnthropicContent> content { get; set; } = new List<AnthropicContent>();
    }

    public class AnthropicContent
    {
        public string text { get; set; } = string.Empty;
    }

    public class GeminiResponse
    {
        public List<GeminiCandidate> candidates { get; set; } = new List<GeminiCandidate>();
    }

    public class GeminiCandidate
    {
        public GeminiContent content { get; set; } = new GeminiContent();
    }

    public class GeminiContent
    {
        public List<GeminiPart> parts { get; set; } = new List<GeminiPart>();
    }

    public class GeminiPart
    {
        public string text { get; set; } = string.Empty;
    }

    public class OllamaResponse
    {
        public string response { get; set; } = string.Empty;
    }

    #endregion
} 