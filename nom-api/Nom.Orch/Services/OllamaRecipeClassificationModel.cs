using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Line and recipe classification from the self-hosted Ollama model, configured like
    /// <see cref="OllamaRecipeRepairModel"/> (Ai:BatchOllamaUrl, else Ai:OllamaUrl; model Ai:RepairModel,
    /// else Ai:Model, else qwen2.5:14b-instruct) so the GPU keeps one model resident. Temperature 0,
    /// JSON answers; anything unreadable counts as no answer.
    /// </summary>
    public class OllamaRecipeClassificationModel : IRecipeClassificationModel
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public OllamaRecipeClassificationModel(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["Ai:RepairModel"] is { Length: > 0 } repair ? repair
                : config["Ai:Model"] is { Length: > 0 } model ? model
                : "qwen2.5:14b-instruct";
        }

        public bool IsConfigured => _baseUrl.Length > 0;
        public string ModelName { get; }

        public async Task<IReadOnlyDictionary<int, string>> ClassifyLinesAsync(string recipeName, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
        {
            if (lines.Count == 0) return new Dictionary<int, string>();

            var prompt =
                $"These ingredient lines from the recipe {JsonSerializer.Serialize(recipeName)} have no amount. For each numbered line decide " +
                "which ONE kind of line it is:\n" +
                "- \"ingredient\": a real food to be added whose amount is simply not written (e.g. \"flour\", \"fresh butter\", \"breadcrumbs\").\n" +
                "- \"reference\": a component made by another recipe or prepared separately, such as a sauce, gravy, stock, broth, forcemeat, " +
                "stuffing, paste, glaze, batter or \"custard stuff\". Bought or bottled foods (ketchup, vinegar, mustard, jam) and plain " +
                "foods used for coating or frying (egg and bread-crumbs, lard) are ingredients, not references.\n" +
                "- \"equipment\": a utensil, vessel or tool (a pan, mould, sieve, cloth, skewer, bain-marie).\n" +
                "- \"note\": a yield or serving note, a heading or a remark; nothing to add.\n" +
                "- \"instruction\": a sentence of the method (telling the cook what to do) stored by mistake as an ingredient.\n" +
                "- \"artefact\": a leftover of a data merge such as \"butter + butter\", or empty or garbled text.\n" +
                "If you are unsure, answer \"ingredient\". Reply with STRICT JSON only: {\"lines\":[{\"n\":1,\"kind\":\"...\"}]} with one entry per line.\n\n" +
                "LINES:\n" + string.Join("\n", lines.Select((l, i) => $"{i + 1}. {JsonSerializer.Serialize(l)}"));

            return ParseLineKinds(await GenerateAsync(prompt, cancellationToken), lines.Count);
        }

        public async Task<FoodVerdict> IsFoodAsync(string recipeName, IReadOnlyList<string> ingredientLines, CancellationToken cancellationToken = default)
        {
            var prompt =
                "Below is an entry from an old household cookbook, which mixes cookery with household hints. Is this entry a recipe for " +
                "something people eat or drink (including preserving, curing, pickling or brewing food and drink)? Cleaning, polishing, " +
                "stain removal, laundry, cosmetics, toiletries, dyes, glue, pest control and medicines or remedies for ailments are NOT food. " +
                "Answer \"yes\" if it is an edible food or drink recipe, \"no\" if it is not, and \"unsure\" if you cannot tell. Reply with " +
                "STRICT JSON only: {\"answer\":\"yes\"|\"no\"|\"unsure\"}.\n\n" +
                $"TITLE: {JsonSerializer.Serialize(recipeName)}\nINGREDIENTS:\n" +
                string.Join("\n", ingredientLines.Take(40).Select(l => $"- {l}"));

            return ParseFoodVerdict(await GenerateAsync(prompt, cancellationToken));
        }

        public static IReadOnlyDictionary<int, string> ParseLineKinds(string? response, int lineCount)
        {
            var kinds = new Dictionary<int, string>();
            using var doc = Parse(response);
            if (doc == null || !doc.RootElement.TryGetProperty("lines", out var answers) || answers.ValueKind != JsonValueKind.Array) return kinds;

            foreach (var answer in answers.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
            {
                if (!answer.TryGetProperty("n", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out var number)) continue;
                if (!answer.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String) continue;
                var text = kind.GetString()?.Trim().ToLowerInvariant();
                if (number < 1 || number > lineCount || !RecipeIngredientLineKind.All.Contains(text)) continue;
                if (!kinds.TryAdd(number - 1, text!) && kinds[number - 1] != text) kinds[number - 1] = string.Empty;
            }

            foreach (var conflicting in kinds.Where(k => k.Value.Length == 0).Select(k => k.Key).ToList()) kinds.Remove(conflicting);
            return kinds;
        }

        public static FoodVerdict ParseFoodVerdict(string? response)
        {
            using var doc = Parse(response);
            if (doc == null || !doc.RootElement.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String) return FoodVerdict.Unsure;
            return answer.GetString()?.Trim().ToLowerInvariant() switch
            {
                "yes" => FoodVerdict.Food,
                "no" => FoodVerdict.NotFood,
                _ => FoodVerdict.Unsure,
            };
        }

        private static JsonDocument? Parse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;
            try
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
                doc.Dispose();
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for recipe classification.");

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
            return body.TryGetProperty("response", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        }
    }
}
