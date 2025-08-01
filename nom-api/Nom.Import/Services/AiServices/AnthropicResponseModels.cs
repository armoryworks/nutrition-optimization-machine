// File: nom-api/Nom.Import/Services/AiServices/AnthropicResponseModels.cs

using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContent>? Content { get; set; }
    }

    public class AnthropicContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
} 