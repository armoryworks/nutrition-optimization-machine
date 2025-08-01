// File: nom-api/Nom.Import/Services/AiServices/OllamaResponseModel.cs

using System.Text.Json.Serialization;

namespace Nom.Import.Services.AiServices
{
    public class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
} 