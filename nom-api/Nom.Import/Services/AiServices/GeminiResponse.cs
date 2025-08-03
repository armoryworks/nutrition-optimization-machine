// File: nom-api/Nom.Import/Services/AiServices/GeminiResponse.cs

using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }
} 