using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiMessage? Message { get; set; }
    }
} 