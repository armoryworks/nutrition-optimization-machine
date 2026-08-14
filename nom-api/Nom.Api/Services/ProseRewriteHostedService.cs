using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nom.Data;

namespace Nom.Api.Services
{
    /// <summary>
    /// Clears the copyright quarantine on scraped recipes by rewriting their
    /// verbatim source prose (description + step text) in original words via a
    /// self-hosted model. Facts are preserved, nothing is invented, the source
    /// text remains in recipe.ScrapedDocument for provenance, and the recipe
    /// still requires normal curation approval before it can publish — this
    /// worker only removes the ContainsSourceProse blocker.
    ///
    /// Off unless Ai:BatchOllamaUrl is configured (point it at the batch-lane
    /// Ollama host, not the interactive one). ProseRewrite:IntervalMinutes
    /// (default 30) and ProseRewrite:BatchSize (default 10) tune the pace.
    /// </summary>
    public class ProseRewriteHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProseRewriteHostedService> _logger;

        public ProseRewriteHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<ProseRewriteHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var baseUrl = (_configuration["Ai:BatchOllamaUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogInformation("Prose rewrite is disabled (Ai:BatchOllamaUrl not configured)");
                return;
            }

            var model = _configuration["Ai:RewriteModel"] ?? _configuration["Ai:Model"] ?? "qwen2.5:3b";
            var interval = TimeSpan.FromMinutes(
                Math.Max(1, _configuration.GetValue("ProseRewrite:IntervalMinutes", 30)));
            var batchSize = Math.Clamp(_configuration.GetValue("ProseRewrite:BatchSize", 10), 1, 100);

            _logger.LogInformation(
                "Prose rewrite enabled: {Url} ({Model}), every {Minutes}m, up to {Batch} recipes per sweep",
                baseUrl, model, interval.TotalMinutes, batchSize);

            await SafeDelay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(baseUrl, model, batchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prose rewrite sweep failed");
                }

                await SafeDelay(interval, stoppingToken);
            }
        }

        private async Task SweepAsync(string baseUrl, string model, int batchSize, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var quarantined = await db.Recipes
                .Include(r => r.RecipeSteps)
                .Where(r => r.ContainsSourceProse)
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (quarantined.Count == 0)
            {
                return;
            }

            var rewritten = 0;
            foreach (var recipe in quarantined)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var steps = recipe.RecipeSteps?.OrderBy(s => s.StepNumber).ToList() ?? new();
                var payload = new
                {
                    name = recipe.Name,
                    description = recipe.Description ?? string.Empty,
                    steps = steps.Select(s => s.Description).ToList(),
                };

                var prompt =
                    "Rewrite this recipe's description and steps in fresh, plain wording. " +
                    "Preserve every fact — ingredients, amounts, temperatures, times, order, technique. " +
                    "Invent nothing; drop storytelling and personal anecdotes. Keep the same NUMBER of steps, " +
                    "one rewritten step per original step, same order. Reply with STRICT JSON: " +
                    "{\"description\": str, \"steps\": [str, ...]} with exactly " +
                    $"{steps.Count} steps.\n\nRECIPE:\n{JsonSerializer.Serialize(payload)}";

                try
                {
                    var http = _httpClientFactory.CreateClient(nameof(ProseRewriteHostedService));
                    http.Timeout = TimeSpan.FromSeconds(600);
                    var response = await http.PostAsJsonAsync($"{baseUrl}/api/generate", new
                    {
                        model,
                        prompt,
                        stream = false,
                        format = "json",
                        options = new { temperature = 0.2 },
                    }, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                    var answer = JsonSerializer.Deserialize<RewritePayload>(
                        body.GetProperty("response").GetString() ?? "{}",
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (answer?.Steps == null || answer.Steps.Count != steps.Count ||
                        answer.Steps.Any(string.IsNullOrWhiteSpace))
                    {
                        _logger.LogWarning(
                            "Rewrite for recipe {Id} returned {Got} steps for {Want}; leaving quarantined",
                            recipe.Id, answer?.Steps?.Count ?? 0, steps.Count);
                        continue;
                    }

                    recipe.Description = string.IsNullOrWhiteSpace(answer.Description)
                        ? recipe.Description
                        : answer.Description.Trim();
                    for (var i = 0; i < steps.Count; i++)
                    {
                        steps[i].Description = answer.Steps[i].Trim();
                        steps[i].LastModifiedDate = DateTime.UtcNow;
                    }

                    // Quarantine cleared; curation approval still gates publish.
                    recipe.ContainsSourceProse = false;
                    recipe.LastModifiedDate = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    rewritten++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Rewrite failed for recipe {Id}; leaving quarantined", recipe.Id);
                }
            }

            _logger.LogInformation("Prose rewrite sweep: {Rewritten}/{Seen} recipes cleared",
                rewritten, quarantined.Count);
        }

        private static async Task SafeDelay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        private sealed class RewritePayload
        {
            public string? Description { get; set; }
            public List<string>? Steps { get; set; }
        }
    }
}
