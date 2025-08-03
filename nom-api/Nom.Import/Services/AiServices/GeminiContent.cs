using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart>? Parts { get; set; }
    }
} 