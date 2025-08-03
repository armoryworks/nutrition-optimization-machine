using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class AnthropicContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
} 