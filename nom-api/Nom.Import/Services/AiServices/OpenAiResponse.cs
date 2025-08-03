// File: nom-api/Nom.Import/Services/AiServices/OpenAiResponse.cs

using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class OpenAiResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; set; }
    }
} 