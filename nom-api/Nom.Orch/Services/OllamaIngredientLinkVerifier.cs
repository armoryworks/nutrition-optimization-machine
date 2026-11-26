using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        public const int CurrentPromptVersion = 3;

        private static readonly (string Word, LinkJudgement Judgement)[] Verdicts =
        {
            ("same", LinkJudgement.Same),
            ("variety", LinkJudgement.VarietyOrForm),
            ("different", LinkJudgement.DifferentFood),
            ("several", LinkJudgement.MoreThanOneFood),
            ("unsure", LinkJudgement.Unsure),
        };

        private const string Instructions =
            "You check links between recipe ingredients and USDA foods for a nutrition database. Each numbered item gives an ingredient " +
            "as a recipe names it (amounts already taken off) and one USDA description. Say whether the USDA food is what a home cook " +
            "buys and uses for that ingredient, so that its nutrition stands for it.\n" +
            "Read the ingredient first:\n" +
            "- Sizes, praise, temperature and what the cook does to it (large, small, good, fresh, cold, boiling, melted, softened, " +
            "chopped, sliced, diced, minced, grated, beaten, sifted, peeled, divided, for garnish) never change the food.\n" +
            "- An ingredient that names no variety, colour or form means the food as it is usually sold, and the plain or the most common " +
            "everyday entry is that food: any common onion for onion, ripe tomatoes for tomato, table salt for any cooking salt, tap water " +
            "for water, granulated sugar for sugar, ordinary salted butter for butter, whole milk for milk, all-purpose flour for flour, the " +
            "ground spice for a spice named alone or by its whole seed, bark, berry or blade.\n" +
            "- Varieties a cook uses one for another are the same food: any onion, any apple, any potato, cherry or plum tomatoes, " +
            "any cooking salt, any cucumber, any mushroom the ingredient does not name; and so are the forms a cook buys for it: bottled " +
            "lemon or lime juice for the juice, canned or dried beans and chickpeas, whole or blanched nuts.\n" +
            "Then read the description: its first words name the food, the words after the commas its variety, part, form and product.\n" +
            "For each item write food: the ingredient as bought in a few words (without the cook's work), usda: the USDA food in a few " +
            "plain words, then verdict, one of:\n" +
            "  \"same\" - the USDA food is the ingredient, or the ordinary form of it as above;\n" +
            "  \"variety\" - the same kind of food, but the description names something the ingredient does not ask for and that changes " +
            "what it is: a light, reduced-fat, low-fat or nonfat product; low sodium, or without salt when the ingredient asks for salted; " +
            "sweetened or unsweetened against the ingredient; cooked, roasted or fried when the ingredient is raw or names no cooking; " +
            "dried, canned or frozen against fresh; a juice, extract, powder, paste or oil of a food named whole, or the whole food for one " +
            "of those; one particular kind (a named cheese, a cut of meat, a regional preparation) when the ingredient names a different " +
            "kind or leaves the kind open among foods that differ; a part (yolk, white, skin, juice) for the whole, or the whole for a part;\n" +
            "  \"different\" - another food: another plant, animal or fruit with a similar name (sweet peppers are not hot chilies), " +
            "a dish, sauce, drink, spread or mix made from it or containing it, or a seasoning blend for one spice;\n" +
            "  \"several\" - the ingredient names more than one food;\n" +
            "  \"unsure\" - you cannot tell what food the ingredient means.\n" +
            "Say \"same\" only when you would put that USDA food in the cook's basket for the ingredient. A wrong \"same\" costs more " +
            "than any other answer.\n" +
            "Examples, not part of the task:\n" +
            "- \"rutabaga, peeled and cubed\" / Rutabagas, raw: food rutabaga, usda raw rutabaga, same (the cook's work).\n" +
            "- \"quail\" / Quail, meat only, raw: food quail, usda raw quail meat, same.\n" +
            "- \"plantains\" / Plantains, yellow, fried: food plantains, usda fried plantains, variety (cooked; the ingredient names no cooking).\n" +
            "- \"guava juice\" / Guavas, common, raw: food guava juice, usda raw guava, variety (the fruit, not its juice).\n" +
            "- \"kumquat marmalade\" / Kumquats, raw: food kumquat marmalade, usda raw kumquats, different (a spread made from it).\n" +
            "- \"kohlrabi\" / Cabbage, raw: food kohlrabi, usda raw cabbage, different (another vegetable).\n" +
            "Reply with JSON only: {\"results\":[{\"n\":1,\"food\":\"...\",\"usda\":\"...\",\"verdict\":\"same\"}]} with one entry per item.";

        /// <summary>
        /// A hash of what decides the judge's answer apart from the model: its instructions, its
        /// verdict words and its prompt version. The link-audit record carries it with the auditor and
        /// the confirming reader, so a changed prompt needs a new score before an audited tier may go live.
        /// </summary>
        public static readonly string Signature = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join("\n", Instructions, string.Join(",", Verdicts.Select(v => v.Word)), CurrentPromptVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))))[..16].ToLowerInvariant();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly bool _strictSchema;

        [ActivatorUtilitiesConstructor]
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
                options = new { temperature = 0, seed = 1, num_ctx = 8192 },
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
                            food = new { type = "string", maxLength = 60 },
                            usda = new { type = "string", maxLength = 60 },
                            verdict = new { type = "string", @enum = Verdicts.Select(v => v.Word).ToArray() },
                        },
                        required = new[] { "n", "food", "usda", "verdict" },
                    },
                },
            },
            required = new[] { "results" },
        };
    }
}
