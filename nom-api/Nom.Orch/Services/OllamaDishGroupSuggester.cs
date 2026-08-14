using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// AI-backed dish canonicalization against a self-hosted Ollama instance
    /// (config: Ai:OllamaUrl + Ai:GroupingModel, falling back to Ai:Model).
    /// Self-contained HTTP on purpose — no dependency on other AI plumbing, so
    /// the class compiles wherever it's dropped. Falls back to the heuristic
    /// suggester per batch on any model failure.
    /// </summary>
    public class OllamaDishGroupSuggester : IDishGroupSuggester
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OllamaDishGroupSuggester> _logger;
        private readonly string _baseUrl;
        private readonly string _model;

        private const string Prompt =
            "Classify each recipe below into a canonical dish group — the plain, generic name " +
            "a cookbook index would use (e.g. \"chocolate chip cookies\", \"butter chicken\", " +
            "\"spaghetti bolognese\", \"banana bread\"). Strip branding, superlatives and dietary " +
            "qualifiers unless they define the dish. Lowercase. Reply with STRICT JSON: " +
            "{\"groups\": [\"<group for #1>\", \"<group for #2>\", ...]} — exactly one entry per " +
            "numbered recipe, in order.\n\n";

        public OllamaDishGroupSuggester(HttpClient httpClient, IConfiguration config, ILogger<OllamaDishGroupSuggester> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _baseUrl = (config["Ai:OllamaUrl"] ?? string.Empty).TrimEnd('/');
            _model = config["Ai:GroupingModel"] ?? config["Ai:Model"] ?? "qwen2.5:3b";
        }

        public async Task<List<string?>> SuggestAsync(List<DishGroupCandidate> candidates, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_baseUrl) || candidates.Count == 0)
            {
                return await new HeuristicDishGroupSuggester().SuggestAsync(candidates, cancellationToken);
            }

            var listing = string.Join("\n", candidates.Select((c, i) =>
                $"{i + 1}. {c.Name} — key ingredients: {string.Join(", ", c.IngredientNames.Take(8))}"));

            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", new
                {
                    model = _model,
                    prompt = Prompt + listing,
                    stream = false,
                    format = "json",
                    options = new { temperature = 0 },
                }, cancellationToken);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                var groups = JsonSerializer.Deserialize<GroupsPayload>(body.GetProperty("response").GetString() ?? "{}")?.Groups;

                if (groups == null || groups.Count != candidates.Count)
                {
                    throw new InvalidOperationException(
                        $"model returned {groups?.Count ?? 0} groups for {candidates.Count} recipes");
                }

                return groups
                    .Select(g => HeuristicDishGroupSuggester.Normalize(g ?? string.Empty))
                    .ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama dish grouping failed; using heuristic for this batch");
                return await new HeuristicDishGroupSuggester().SuggestAsync(candidates, cancellationToken);
            }
        }

        private sealed class GroupsPayload
        {
            [System.Text.Json.Serialization.JsonPropertyName("groups")]
            public List<string?>? Groups { get; set; }
        }
    }
}
