using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class OpenAiMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
} 