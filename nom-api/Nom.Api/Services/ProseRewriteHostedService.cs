using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Services;

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
    /// (default 10) and ProseRewrite:BatchSize (default 25) tune the pace;
    /// ProseRewrite:RetryDays (default 30) is how long a recipe the model could
    /// not rewrite rests before it is tried again.
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
                Math.Max(1, _configuration.GetValue("ProseRewrite:IntervalMinutes", 10)));
            var batchSize = Math.Clamp(_configuration.GetValue("ProseRewrite:BatchSize", 25), 1, 100);
            var retryDays = Math.Max(1, _configuration.GetValue("ProseRewrite:RetryDays", 30));

            _logger.LogInformation(
                "Prose rewrite enabled: {Url} ({Model}), every {Minutes}m, up to {Batch} recipes per sweep",
                baseUrl, model, interval.TotalMinutes, batchSize);

            await SafeDelay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await AiQuietHours.WaitAsync(_configuration, _logger, stoppingToken)) return;
                try
                {
                    await SweepAsync(baseUrl, model, batchSize, retryDays, stoppingToken);
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

        private async Task SweepAsync(string baseUrl, string model, int batchSize, int retryDays, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var retryBefore = DateTime.UtcNow.AddDays(-retryDays);

            var quarantined = await db.Recipes
                .Include(r => r.RecipeSteps)
                .Where(r => r.ContainsSourceProse && (r.ProseRewriteAttemptedAt == null || r.ProseRewriteAttemptedAt < retryBefore))
                .OrderBy(r => r.ProseRewriteAttemptedAt == null ? 0 : 1)
                .ThenBy(r => r.Id)
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
                recipe.ProseRewriteAttemptedAt = DateTime.UtcNow;

                var steps = recipe.RecipeSteps?.Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).ToList() ?? new();
                try
                {
                    var answer = await RewriteAsync(baseUrl, model, recipe, steps, cancellationToken);
                    if (answer == null)
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }

                    recipe.Description = string.IsNullOrWhiteSpace(answer.Description)
                        ? recipe.Description
                        : answer.Description.Trim();
                    if (steps.Count == 1 && answer.Steps!.Count > 1)
                    {
                        Nom.Orch.Services.Support.RecipeStepSplit.Apply(recipe, steps[0], answer.Steps.Select(s => s.Trim()).ToList());
                    }
                    else
                    {
                        for (var i = 0; i < steps.Count; i++)
                        {
                            steps[i].Description = answer.Steps![i].Trim();
                            steps[i].LastModifiedDate = DateTime.UtcNow;
                            steps[i].LastModifiedByPersonId = SystemConstants.SystemPersonId;
                        }
                    }

                    recipe.ContainsSourceProse = false;
                    recipe.LastModifiedDate = DateTime.UtcNow;
                    recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
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
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            _logger.LogInformation("Prose rewrite sweep: {Rewritten}/{Seen} recipes cleared",
                rewritten, quarantined.Count);
        }

        /// <summary>
        /// Asks the model once, and once more with the step count spelled out when its answer is off
        /// by one. A one-step original may come back as several steps, since old cookbooks wrote the
        /// whole method as one paragraph; anything else must keep the step count.
        /// </summary>
        private async Task<RewritePayload?> RewriteAsync(string baseUrl, string model, Nom.Data.Recipe.RecipeEntity recipe,
            List<Nom.Data.Recipe.RecipeStepEntity> steps, CancellationToken cancellationToken)
        {
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

            var answer = await GenerateAsync(baseUrl, model, prompt, cancellationToken);
            if (Acceptable(answer, steps.Count)) return answer;

            var got = answer?.Steps?.Count ?? 0;
            if (answer?.Steps != null && Math.Abs(got - steps.Count) == 1 && steps.Count > 1)
            {
                answer = await GenerateAsync(baseUrl, model,
                    prompt + $"\n\nYour previous answer had {got} steps. Reply again with EXACTLY {steps.Count} steps.", cancellationToken);
                if (Acceptable(answer, steps.Count)) return answer;
                got = answer?.Steps?.Count ?? 0;
            }

            _logger.LogWarning("Rewrite for recipe {Id} returned {Got} steps for {Want}; leaving quarantined", recipe.Id, got, steps.Count);
            return null;
        }

        private static bool Acceptable(RewritePayload? answer, int originalSteps) =>
            answer?.Steps != null
            && answer.Steps.Count > 0
            && (answer.Steps.Count == originalSteps || originalSteps == 1)
            && !answer.Steps.Any(string.IsNullOrWhiteSpace);

        private async Task<RewritePayload?> GenerateAsync(string baseUrl, string model, string prompt, CancellationToken cancellationToken)
        {
            var http = _httpClientFactory.CreateClient(nameof(ProseRewriteHostedService));
            http.Timeout = TimeSpan.FromSeconds(600);
            var response = await http.PostAsJsonAsync($"{baseUrl}/api/generate", new
            {
                model,
                prompt,
                stream = false,
                format = "json",
                options = new { temperature = 0.2, num_predict = 2048 },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return JsonSerializer.Deserialize<RewritePayload>(
                body.GetProperty("response").GetString() ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
