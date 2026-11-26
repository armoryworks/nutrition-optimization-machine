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
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Ingredient → USDA food matching against the local Ollama model (Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl; model Ai:LinkModel, else Ai:Model). The model names the food an ingredient
    /// means, then answers with a candidate number or 0 for none, among options that hold one entry
    /// per USDA description; every answer becomes an admin-reviewed proposal, never a direct change.
    /// The answer is constrained by a JSON schema (Ai:StrictSchema, default true; false falls back to
    /// plain JSON mode), and an entry is read only by its own number, never by its position.
    /// </summary>
    public class OllamaIngredientLinkMatcher : IIngredientLinkMatcher
    {
        public const int CurrentPromptVersion = 3;

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
            questions = questions.Select(q => q with { Candidates = Options(q.Candidates) }).ToList();

            var prompt =
                "Link each recipe ingredient to the USDA food it means, as a home cook buys it, or to none. For each ingredient:\n" +
                "1. food: name the food in a few words. Leave out amounts, sizes (large, small, a dozen), praise (good, fine, best) and " +
                "what the cook does to it (chopped, sliced, grated, beaten, melted, softened, cubed, toasted, peeled, drained, for garnish, " +
                "at room temperature). Keep every word that names its kind, variety, part, form or flavour (smoked, canned, dried, frozen, " +
                "cooked, sweetened, low-fat, unsalted). A whole egg beaten or separated is still a whole egg; 'the size of an egg' is an amount.\n" +
                "2. pick: the number of the option that is that food. When several are, take the most general one: raw for produce, meat " +
                "and fish; dry for pasta, rice, grains and dried beans; the regular, unflavoured product; USDA's 'all commercial varieties' " +
                "or 'year round average' entries. Do not take an option that adds a variety, brand, flavour, cooking, fat level or packing " +
                "the food does not name while an option without it fits. A variety the options lack links to the plain food " +
                "(a named apple variety to the general apple entry).\n" +
                "3. pick 0 when no option is that food: a different plant, animal or part; something made from it or with it (a juice, oil, " +
                "sauce, spread, dish, drink, candy, blend or mix: beet juice is not beets, truffle oil is not truffles, five-spice powder is " +
                "not any one spice); a different fruit or a flavoured product (yuzu juice is not lemon juice, garlic-herb cheese is not plain " +
                "cheese); or two foods named together. A wrong link costs more than none.\n" +
                "Examples, not part of the task:\n" +
                "- \"medium rutabaga, peeled and diced\" with options 1. Rutabagas, cooked, boiled, drained, without salt 2. Rutabagas, raw " +
                "3. Rutabagas, mashed: food \"rutabaga\", pick 2 (peeling and dicing are the cook's work).\n" +
                "- \"dried persimmons\" with options 1. Persimmons, japanese, raw 2. Persimmons, japanese, dried: food \"dried persimmons\", " +
                "pick 2 (the form named).\n" +
                "- \"yuzu juice\" with options 1. Lemon juice, bottled, unsweetened 2. Lime juice, bottled, unsweetened: food \"yuzu juice\", " +
                "pick 0 (another fruit).\n" +
                "confidence: 0.0-1.0, how sure you are that the pick is right.\n" +
                "Reply with JSON only: {\"answers\":[{\"n\":1,\"food\":\"...\",\"pick\":2,\"confidence\":0.9}]}, one entry per ingredient, " +
                "n being its # number.\n\n" +
                string.Join("\n\n", questions.Select((q, i) =>
                    $"#{i + 1} \"{q.Name}\"\n" + string.Join("\n", q.Candidates.Select((c, j) => $"  {j + 1}. {c.Name}"))));

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", new
            {
                model = ModelName,
                prompt,
                stream = false,
                format = _strictSchema ? Schema(questions) : "json",
                options = new { temperature = 0, seed = 1, num_ctx = 8192 },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return Read(body.GetProperty("response").GetString() ?? "{}", questions);
        }

        /// <summary>
        /// The options a question is asked with: its candidates in order, with one entry for each
        /// USDA description. Two entries the model cannot tell apart (an SR Legacy food and the
        /// Foundation food of the same description) are asked as the Foundation food, USDA's current
        /// analysed entry.
        /// </summary>
        public static IReadOnlyList<FoodCandidate> Options(IReadOnlyList<FoodCandidate> candidates) =>
            candidates.GroupBy(c => c.Name.Trim().ToLowerInvariant(), StringComparer.Ordinal)
                .Select(g => g.FirstOrDefault(c => c.DataType == Nom.Data.Nutrition.CatalogAdmissionRule.FoundationFood) ?? g.First())
                .ToList();

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
                            food = new { type = "string", maxLength = 60 },
                            pick = new { type = "integer", minimum = 0, maximum = questions.Max(q => q.Candidates.Count) },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "n", "food", "pick", "confidence" },
                    },
                },
            },
            required = new[] { "answers" },
        };

        private static int? Int(JsonElement e, string property) =>
            e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }
}
