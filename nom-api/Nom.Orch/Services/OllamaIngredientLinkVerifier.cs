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
    /// matcher; model IngredientLinking:VerifyModel, with no default: unset means no second opinion).
    /// It judges one pair at a time — the wording and the one USDA description the pick chose — and
    /// answers same food, different variety or form, different food, more than one food, or unsure.
    /// The answer is constrained by a JSON schema (Ai:StrictSchema, default true) and read only by
    /// its number; a pair it does not answer readably is left out.
    /// </summary>
    public class OllamaIngredientLinkVerifier : IIngredientLinkVerifier
    {
        public const int CurrentPromptVersion = 2;

        private static readonly (string Word, LinkJudgement Judgement)[] Verdicts =
        {
            ("same", LinkJudgement.Same),
            ("variety", LinkJudgement.VarietyOrForm),
            ("different", LinkJudgement.DifferentFood),
            ("several", LinkJudgement.MoreThanOneFood),
            ("unsure", LinkJudgement.Unsure),
        };

        private const string Instructions =
            "You audit links between recipe ingredients and USDA food descriptions. Each numbered item gives a recipe ingredient " +
            "and one USDA description. Say whether a home cook who buys the ingredient would buy the food the description names.\n" +
            "Answer one word per item:\n" +
            "  \"same\" - the same food, as bought (the plain, general-purpose entry counts as the same food when the ingredient names no form);\n" +
            "  \"variety\" - the same kind of food but a variety, color, cut or form (dried, canned, cooked, frozen, ground, powdered) the ingredient rules out or does not name;\n" +
            "  \"different\" - a different food, a dish or sauce made from it, or a product that merely contains it;\n" +
            "  \"several\" - the ingredient names more than one food (for example 'salt and pepper', 'butter or margarine');\n" +
            "  \"unsure\" - you cannot tell.\n" +
            "Reply with STRICT JSON only: {\"results\":[{\"n\":1,\"verdict\":\"same\"}]} with one entry per item.";

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly bool _strictSchema;

        public OllamaIngredientLinkVerifier(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["IngredientLinking:VerifyModel"]?.Trim() ?? string.Empty;
            _strictSchema = config.GetValue("Ai:StrictSchema", true);
        }

        /// <summary>The same judge asked of a named model on a named host, for a lane that reads it as an auditor or a confirming reader.</summary>
        public OllamaIngredientLinkVerifier(HttpClient httpClient, string baseUrl, string model, bool strictSchema = true)
        {
            _httpClient = httpClient;
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            ModelName = model?.Trim() ?? string.Empty;
            _strictSchema = strictSchema;
        }

        public bool IsConfigured => _baseUrl.Length > 0 && ModelName.Length > 0;
        public string ModelName { get; }
        public int PromptVersion => CurrentPromptVersion;

        public async Task<IReadOnlyList<LinkVerdict>> JudgeAsync(IReadOnlyList<LinkPair> pairs, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL or IngredientLinking:VerifyModel configured for ingredient link verification.");
            if (pairs.Count == 0) return Array.Empty<LinkVerdict>();

            var body = string.Join("\n\n", pairs.Select((p, i) =>
                $"Item {i + 1}\n  ingredient: \"{p.Wording}\"\n  USDA: {p.Description}"));

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", new
            {
                model = ModelName,
                stream = false,
                format = _strictSchema ? Schema(pairs.Count) : "json",
                options = new { temperature = 0, num_ctx = 8192 },
                messages = new[]
                {
                    new { role = "system", content = Instructions },
                    new { role = "user", content = body },
                },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return Read(reply.GetProperty("message").GetProperty("content").GetString() ?? "{}", pairs);
        }

        /// <summary>
        /// The verdicts in a model reply, each found by its number. An entry whose number is missing,
        /// repeated or out of range, or whose verdict is not one of the five words, is left out.
        /// Throws when the reply holds no results at all.
        /// </summary>
        public static IReadOnlyList<LinkVerdict> Read(string reply, IReadOnlyList<LinkPair> pairs)
        {
            using var doc = JsonDocument.Parse(reply);
            var entries = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no results.");

            var verdicts = new List<LinkVerdict>();
            for (var i = 0; i < pairs.Count; i++)
            {
                var number = i + 1;
                var numbered = entries.Where(e => e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var v) && v == number).ToList();
                if (numbered.Count != 1) continue;

                var word = numbered[0].TryGetProperty("verdict", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString()?.Trim().ToLowerInvariant() : null;
                var match = Verdicts.Where(v => v.Word == word).Select(v => (LinkJudgement?)v.Judgement).FirstOrDefault();
                if (match is { } judgement) verdicts.Add(new LinkVerdict(pairs[i].IngredientId, judgement));
            }
            return verdicts;
        }

        private static object Schema(int count) => new
        {
            type = "object",
            properties = new
            {
                results = new
                {
                    type = "array",
                    minItems = count,
                    maxItems = count,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            n = new { type = "integer", minimum = 1, maximum = count },
                            verdict = new { type = "string", @enum = Verdicts.Select(v => v.Word).ToArray() },
                        },
                        required = new[] { "n", "verdict" },
                    },
                },
            },
            required = new[] { "results" },
        };
    }
}
