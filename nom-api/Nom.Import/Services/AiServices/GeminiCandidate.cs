using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }
} 