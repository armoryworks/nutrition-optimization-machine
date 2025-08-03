// File: nom-api/Nom.Import/Services/AiServices/AnthropicResponse.cs

using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContent>? Content { get; set; }
    }
} 