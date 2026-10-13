using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Recipe equipment tagging against a self-hosted Ollama instance (config: Ai:BatchOllamaUrl,
    /// falling back to Ai:OllamaUrl; model Ai:ToolTaggingModel, else the instance's Ai:Model so the GPU
    /// keeps one model resident instead of swapping, else qwen2.5:7b-instruct).
    /// The model may only pick from the supplied tool keys; anything else it names comes back
    /// as a proposal for the admin suggestion queue, never as a tool.
    /// </summary>
    public class OllamaRecipeToolTagger : IRecipeToolTagger
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public OllamaRecipeToolTagger(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = (config["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            ModelName = config["Ai:ToolTaggingModel"] ?? config["Ai:Model"] ?? "qwen2.5:7b-instruct";
        }

        public bool IsConfigured => _baseUrl.Length > 0;
        public string ModelName { get; }

        private static readonly string[] Categories =
        {
            KitchenToolCatalog.CategoryCooking, KitchenToolCatalog.CategoryAppliances, KitchenToolCatalog.CategoryCookware,
            KitchenToolCatalog.CategoryOutdoor, KitchenToolCatalog.CategoryGadgets, KitchenToolCatalog.CategoryOther,
        };

        public async Task<IReadOnlyList<ToolTagResult>> TagAsync(
            IReadOnlyList<ToolTagCandidate> recipes,
            IReadOnlyDictionary<string, string> knownTools,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for tool tagging.");
            if (recipes.Count == 0) return Array.Empty<ToolTagResult>();

            var prompt =
                "You identify the cooking equipment each recipe needs in order to be made as written.\n" +
                "KNOWN TOOLS (use these exact keys): " +
                string.Join(", ", knownTools.Select(t => $"{t.Key} ({t.Value})")) + ".\n" +
                "Rules:\n" +
                "- \"tools\": known keys a step of the recipe actually uses (baking, roasting or broiling uses \"oven\"; " +
                "boiling, simmering or frying uses \"stovetop\"). Never guess: if no step uses a tool, leave it out. " +
                "Skip anything optional or merely suggested. No-cook recipes have none. " +
                "Name a specialized appliance (rice cooker, slow cooker, air fryer, instant-pot, sous vide) only when a step explicitly names it — " +
                "\"cook the rice\" means \"stovetop\".\n" +
                "- \"other\": specialty equipment the recipe needs that is NOT a known tool and NOT everyday kit " +
                "(never knives, boards, bowls, spoons, spatulas, whisks, measuring cups, ordinary pots, pans, sheets, foil, " +
                "parchment, racks, jars). Short singular noun, e.g. \"pizza stone\", \"tortilla press\", \"bundt pan\". " +
                "category is one of: " + string.Join(", ", Categories.Select(c => $"\"{c}\"")) + ".\n" +
                "Reply with STRICT JSON only: {\"recipes\":[{\"n\":1,\"tools\":[\"oven\"],\"other\":[{\"name\":\"pizza stone\",\"category\":\"" +
                KitchenToolCatalog.CategoryCookware + "\"}]}]} — one entry per numbered recipe.\n\n" +
                string.Join("\n\n", recipes.Select((r, i) => $"#{i + 1} {r.Name}\n{r.StepText}"));

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
                && doc.RootElement.TryGetProperty("recipes", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
                    : new List<JsonElement>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no recipe entries.");

            var results = new List<ToolTagResult>();
            for (var i = 0; i < recipes.Count; i++)
            {
                var number = i + 1;
                JsonElement? entry = entries.FirstOrDefault(e => e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var v) && v == number) is { ValueKind: JsonValueKind.Object } found
                    ? found
                    : entries.Count == recipes.Count ? entries[i] : null;
                if (entry is not { } e) continue;

                var known = Strings(e, "tools")
                    .Select(k => k.Trim().ToLowerInvariant())
                    .Where(knownTools.ContainsKey)
                    .Distinct()
                    .ToList();
                var other = new List<ProposedTool>();
                if (e.TryGetProperty("other", out var others) && others.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in others.EnumerateArray())
                    {
                        var name = o.ValueKind == JsonValueKind.String ? o.GetString()
                            : o.ValueKind == JsonValueKind.Object && o.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()
                            : null;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var category = o.ValueKind == JsonValueKind.Object && o.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        other.Add(new ProposedTool(name.Trim(), Categories.Contains(category) ? category : null));
                    }
                }
                results.Add(new ToolTagResult(recipes[i].RecipeId, known, other));
            }
            return results;
        }

        private static IEnumerable<string> Strings(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)
                : Enumerable.Empty<string>();
    }
}
