using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// RequiresRevision repair proposals from the self-hosted Ollama model (Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl; model Ai:RepairModel, else Ai:Model so the GPU keeps one model resident, else
    /// qwen2.5:14b-instruct). One request at a time; answers are untrusted until grounded.
    /// </summary>
    public class OllamaRecipeRepairModel : IRecipeRepairModel
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public OllamaRecipeRepairModel(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["Ai:RepairModel"] is { Length: > 0 } repair ? repair
                : config["Ai:Model"] is { Length: > 0 } model ? model
                : "qwen2.5:14b-instruct";
        }

        public bool IsConfigured => _baseUrl.Length > 0;
        public string ModelName { get; }

        public async Task<IReadOnlyList<string>?> SplitStepsAsync(string method, CancellationToken cancellationToken = default)
        {
            var prompt =
                "Split this recipe method into its separate steps, in order. Copy the text EXACTLY: every word, number and " +
                "punctuation mark stays as written, nothing added, removed, reworded or reordered. Only decide where one step ends " +
                "and the next begins; cut only between sentences or at a semicolon. Each step should be one cooking action or a few " +
                "closely related ones. Reply with STRICT JSON only: {\"steps\": [str, ...]}.\n\nMETHOD:\n" + method;

            using var doc = await GenerateAsync(prompt, cancellationToken);
            if (doc == null || !doc.RootElement.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array) return null;
            return steps.EnumerateArray()
                .Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() ?? string.Empty : string.Empty)
                .ToList();
        }

        public async Task<IReadOnlyDictionary<int, string>> QuoteQuantitiesAsync(
            IReadOnlyList<string> ingredientLines, string method, CancellationToken cancellationToken = default)
        {
            var quotes = new Dictionary<int, string>();
            if (ingredientLines.Count == 0) return quotes;

            var prompt =
                "For each numbered ingredient, find where the recipe states HOW MUCH of it to use. Quote that amount EXACTLY as it " +
                "is written in the ingredient line or the method, together with the ingredient word it applies to (e.g. \"half a " +
                "pound of butter\", \"two eggs\", \"3 cups of milk\"). The quote must be a verbatim, contiguous piece of the text. " +
                "If no specific amount is written for that ingredient (vague words like \"a little\", \"some\", \"to taste\" are NOT " +
                "amounts), or you are unsure, answer null. Never estimate, convert or invent an amount. Reply with STRICT JSON only: " +
                "{\"answers\":[{\"n\":1,\"quote\":\"...\"}]} with one entry per ingredient.\n\nINGREDIENTS:\n" +
                string.Join("\n", ingredientLines.Select((l, i) => $"{i + 1}. {JsonSerializer.Serialize(l)}")) +
                "\n\nMETHOD:\n" + method;

            using var doc = await GenerateAsync(prompt, cancellationToken);
            if (doc == null || !doc.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array) return quotes;

            foreach (var answer in answers.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
            {
                if (!answer.TryGetProperty("n", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out var number)) continue;
                if (!answer.TryGetProperty("quote", out var quote) || quote.ValueKind != JsonValueKind.String) continue;
                var text = quote.GetString();
                if (number < 1 || number > ingredientLines.Count || string.IsNullOrWhiteSpace(text)
                    || text.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)) continue;
                quotes[number - 1] = text;
            }
            return quotes;
        }

        private async Task<JsonDocument?> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for recipe repair.");

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", new
            {
                model = ModelName,
                prompt,
                stream = false,
                format = "json",
                options = new { temperature = 0, num_ctx = 8192 },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            try
            {
                var doc = JsonDocument.Parse(body.GetProperty("response").GetString() ?? "{}");
                if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
                doc.Dispose();
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
