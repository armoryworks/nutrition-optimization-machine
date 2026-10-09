using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                "Skip anything optional or merely suggested. No-cook recipes have none.\n" +
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
            var payload = JsonSerializer.Deserialize<Payload>(body.GetProperty("response").GetString() ?? "{}");
            var entries = payload?.Recipes ?? new List<Entry>();
            if (entries.Count == 0) throw new InvalidOperationException("Model returned no recipe entries.");

            var results = new List<ToolTagResult>();
            for (var i = 0; i < recipes.Count; i++)
            {
                var entry = entries.FirstOrDefault(e => e.N == i + 1) ?? (entries.Count == recipes.Count ? entries[i] : null);
                if (entry == null) continue;

                var known = (entry.Tools ?? new List<string>())
                    .Select(k => k?.Trim().ToLowerInvariant() ?? string.Empty)
                    .Where(knownTools.ContainsKey)
                    .Distinct()
                    .ToList();
                var other = (entry.Other ?? new List<OtherEntry>())
                    .Where(o => !string.IsNullOrWhiteSpace(o.Name))
                    .Select(o => new ProposedTool(o.Name!.Trim(), Categories.Contains(o.Category) ? o.Category : null))
                    .ToList();
                results.Add(new ToolTagResult(recipes[i].RecipeId, known, other));
            }
            return results;
        }

        private sealed class Payload
        {
            [JsonPropertyName("recipes")] public List<Entry>? Recipes { get; set; }
        }

        private sealed class Entry
        {
            [JsonPropertyName("n")] public int N { get; set; }
            [JsonPropertyName("tools")] public List<string>? Tools { get; set; }
            [JsonPropertyName("other")] public List<OtherEntry>? Other { get; set; }
        }

        private sealed class OtherEntry
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("category")] public string? Category { get; set; }
        }
    }
}
