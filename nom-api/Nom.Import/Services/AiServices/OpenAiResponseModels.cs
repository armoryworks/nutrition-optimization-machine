// File: nom-api/Nom.Import/Services/AiServices/OpenAiResponseModels.cs

using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class OpenAiResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; set; }
    }

    public class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiMessage? Message { get; set; }
    }

    public class OpenAiMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
} 