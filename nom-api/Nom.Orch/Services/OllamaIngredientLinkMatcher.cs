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
    /// Ingredient → USDA food matching against the local Ollama model (Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl; model Ai:LinkModel, else Ai:Model). The model answers with a candidate number
    /// or 0 for none; every answer becomes an admin-reviewed proposal, never a direct change.
    /// </summary>
    public class OllamaIngredientLinkMatcher : IIngredientLinkMatcher
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public OllamaIngredientLinkMatcher(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["Ai:LinkModel"] ?? config["Ai:Model"] ?? "qwen2.5:7b-instruct";
        }

        public bool IsConfigured => _baseUrl.Length > 0;
        public string ModelName { get; }

        public async Task<IReadOnlyList<LinkAnswer>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for ingredient linking.");
            if (questions.Count == 0) return Array.Empty<LinkAnswer>();

            var prompt =
                "Match each recipe ingredient to the USDA food it most plausibly means, as a home cook would use it.\n" +
                "Rules: pick only from that ingredient's numbered options; answer 0 when none is the same food " +
                "(a different food, a dish, or a product that merely contains it is NOT a match — e.g. 'sugar snap peas' is not sugar). " +
                "Prefer the plain, raw or most common form when the ingredient doesn't specify. " +
                "confidence is 0.0-1.0.\n" +
                "Reply with STRICT JSON only: {\"answers\":[{\"n\":1,\"pick\":2,\"confidence\":0.9}]} — one entry per ingredient.\n\n" +
                string.Join("\n\n", questions.Select((q, i) =>
                    $"#{i + 1} \"{q.Name}\"\n" + string.Join("\n", q.Candidates.Select((c, j) => $"  {j + 1}. {c.Name}"))));

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
            using var doc = JsonDocument.Parse(body.GetProperty("response").GetString() ?? "{}");
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("answers", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no answers.");

            var answers = new List<LinkAnswer>();
            for (var i = 0; i < questions.Count; i++)
            {
                var number = i + 1;
                var entry = entries.FirstOrDefault(e => Int(e, "n") == number);
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    if (entries.Count != questions.Count) continue;
                    entry = entries[i];
                }

                var pick = Int(entry, "pick") ?? 0;
                var confidence = entry.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var cv)
                    ? Math.Clamp(cv, 0m, 1m)
                    : 0.5m;
                var q = questions[i];
                var choice = pick >= 1 && pick <= q.Candidates.Count ? q.Candidates[pick - 1] : null;
                answers.Add(new LinkAnswer(q.IngredientId, choice, confidence));
            }
            return answers;
        }

        private static int? Int(JsonElement e, string property) =>
            e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }
}
