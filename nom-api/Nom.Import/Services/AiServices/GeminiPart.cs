using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
} 