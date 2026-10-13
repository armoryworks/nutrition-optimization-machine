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
    /// Second opinion on ingredient → USDA links from the local Ollama model (same URL as the link
    /// matcher; model IngredientLinking:VerifyModel, else the link model). Framed differently from
    /// the first pick on purpose: a chat-style audit question, candidates lettered and in reverse
    /// order, no confidence asked for. An answer that can't be read names no candidate.
    /// </summary>
    public class OllamaIngredientLinkVerifier : IIngredientLinkVerifier
    {
        private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private const string Instructions =
            "You audit links between recipe ingredients and USDA food descriptions. For each ingredient, name the one lettered USDA " +
            "description that is the same food a home cook means by it, as bought. A different food, a dish or sauce made from it " +
            "(unless the ingredient itself is that dish or sauce), a product that merely contains it, or a variety, color or form the " +
            "ingredient rules out is not the same food. When two could fit, choose the plain, general-purpose one. Answer \"none\" when " +
            "no description is clearly the same food.\n" +
            "Reply with STRICT JSON only: {\"results\":[{\"ingredient\":1,\"letter\":\"B\"}]} with one entry per ingredient; letter is a " +
            "single letter or \"none\".";

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public OllamaIngredientLinkVerifier(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["IngredientLinking:VerifyModel"] is { Length: > 0 } verify
                ? verify
                : config["Ai:LinkModel"] ?? config["Ai:Model"] ?? "qwen2.5:7b-instruct";
        }

        public bool IsConfigured => _baseUrl.Length > 0;
        public string ModelName { get; }

        public async Task<IReadOnlyList<LinkVerdict>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for ingredient link verification.");
            if (questions.Count == 0) return Array.Empty<LinkVerdict>();

            var shown = questions.Select(q => q.Candidates.Take(Letters.Length).Reverse().ToList()).ToList();
            var body = string.Join("\n\n", questions.Select((q, i) =>
                $"Ingredient {i + 1}: \"{q.Name}\"\n" + string.Join("\n", shown[i].Select((c, j) => $"  {Letters[j]}) {c.Name}"))));

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", new
            {
                model = ModelName,
                stream = false,
                format = "json",
                options = new { temperature = 0, num_ctx = 8192 },
                messages = new[]
                {
                    new { role = "system", content = Instructions },
                    new { role = "user", content = body },
                },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            using var doc = JsonDocument.Parse(reply.GetProperty("message").GetProperty("content").GetString() ?? "{}");
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no results.");

            var verdicts = new List<LinkVerdict>();
            for (var i = 0; i < questions.Count; i++)
            {
                var number = i + 1;
                var entry = entries.FirstOrDefault(e => e.TryGetProperty("ingredient", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var v) && v == number);
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    if (entries.Count != questions.Count) continue;
                    entry = entries[i];
                }

                var letter = entry.TryGetProperty("letter", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()?.Trim().TrimEnd(')') : null;
                var index = letter is { Length: 1 } ? Letters.IndexOf(char.ToUpperInvariant(letter[0])) : -1;
                var choice = index >= 0 && index < shown[i].Count ? shown[i][index] : null;
                verdicts.Add(new LinkVerdict(questions[i].IngredientId, choice));
            }
            return verdicts;
        }
    }
}
