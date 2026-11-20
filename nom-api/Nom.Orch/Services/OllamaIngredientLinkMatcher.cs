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
    /// The answer is constrained by a JSON schema (Ai:StrictSchema, default true; false falls back to
    /// plain JSON mode), and an entry is read only by its own number, never by its position.
    /// </summary>
    public class OllamaIngredientLinkMatcher : IIngredientLinkMatcher
    {
        public const int CurrentPromptVersion = 2;

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly bool _strictSchema;

        public OllamaIngredientLinkMatcher(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["Ai:LinkModel"] ?? config["Ai:Model"] ?? "qwen2.5:7b-instruct";
            _strictSchema = config.GetValue("Ai:StrictSchema", true);
        }

        public bool IsConfigured => _baseUrl.Length > 0;
        public string ModelName { get; }
        public int PromptVersion => CurrentPromptVersion;

        public async Task<IReadOnlyList<LinkAnswer>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for ingredient linking.");
            if (questions.Count == 0) return Array.Empty<LinkAnswer>();

            var prompt =
                "Match each recipe ingredient to the USDA food it most plausibly means, as a home cook would use it.\n" +
                "Rules: pick only from that ingredient's numbered options; answer 0 when none is the same food " +
                "(a different food, a dish, or a product that merely contains it is NOT a match — e.g. 'sugar snap peas' is not sugar). " +
                "Prefer an option containing every word of the ingredient (a variety or color like 'black' or 'red' must appear in the option). " +
                "When the ingredient doesn't specify a form, prefer the plain whole food as bought: raw produce (e.g. 'Tomatoes, red, ripe, raw'), " +
                "not juice, canned, cooked or a variety the ingredient didn't name; USDA marks its general-purpose entries " +
                "'year round average' or 'all commercial varieties'. A different plant or a dressing/sauce made with it is not a match. " +
                "confidence is 0.0-1.0.\n" +
                "Reply with STRICT JSON only: {\"answers\":[{\"n\":1,\"pick\":2,\"confidence\":0.9}]} — one entry per ingredient, " +
                "where n is the ingredient's # number.\n\n" +
                string.Join("\n\n", questions.Select((q, i) =>
                    $"#{i + 1} \"{q.Name}\"\n" + string.Join("\n", q.Candidates.Select((c, j) => $"  {j + 1}. {c.Name}"))));

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", new
            {
                model = ModelName,
                prompt,
                stream = false,
                format = _strictSchema ? Schema(questions) : "json",
                options = new { temperature = 0, num_ctx = 8192 },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return Read(body.GetProperty("response").GetString() ?? "{}", questions);
        }

        /// <summary>
        /// The answers in a model reply, each found by its number. An entry whose number is missing,
        /// repeated or out of range, or whose pick is not one of that ingredient's options or 0, is
        /// left out. Throws when the reply holds no answers at all.
        /// </summary>
        public static IReadOnlyList<LinkAnswer> Read(string reply, IReadOnlyList<LinkQuestion> questions)
        {
            using var doc = JsonDocument.Parse(reply);
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("answers", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no answers.");

            var answers = new List<LinkAnswer>();
            for (var i = 0; i < questions.Count; i++)
            {
                var number = i + 1;
                var numbered = entries.Where(e => Int(e, "n") == number).ToList();
                if (numbered.Count != 1) continue;
                var entry = numbered[0];

                var q = questions[i];
                if (Int(entry, "pick") is not { } pick || pick < 0 || pick > q.Candidates.Count) continue;
                var confidence = entry.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var cv)
                    ? Math.Clamp(cv, 0m, 1m)
                    : 0.5m;
                answers.Add(new LinkAnswer(q.IngredientId, pick == 0 ? null : q.Candidates[pick - 1], confidence));
            }
            return answers;
        }

        private static object Schema(IReadOnlyList<LinkQuestion> questions) => new
        {
            type = "object",
            properties = new
            {
                answers = new
                {
                    type = "array",
                    minItems = questions.Count,
                    maxItems = questions.Count,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            n = new { type = "integer", minimum = 1, maximum = questions.Count },
                            pick = new { type = "integer", minimum = 0, maximum = questions.Max(q => q.Candidates.Count) },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "n", "pick", "confidence" },
                    },
                },
            },
            required = new[] { "answers" },
        };

        private static int? Int(JsonElement e, string property) =>
            e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }
}
