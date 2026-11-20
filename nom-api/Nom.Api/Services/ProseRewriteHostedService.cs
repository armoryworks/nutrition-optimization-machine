using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

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
    /// not rewrite rests before it is tried again. ProseRewrite:CheckFidelity
    /// (default true) keeps a rewrite in quarantine unless <see cref="RewriteFidelity"/>
    /// passes it against the text it was given and against the stored source. While a sweep comes
    /// back full the next starts after ProseRewrite:PauseSeconds (default 5) instead of the interval
    /// (ProseRewrite:DrainWhileFull, default on only when Ai:Lanes:prose-rewrite:MaxItemsPerDay is
    /// above 0). A refused connection to the model host stamps
    /// nothing; a timeout stamps the recipe it hit. Either ends the sweep and is reported to the
    /// <see cref="AiLaneGate"/> (lane "prose-rewrite"), which every sweep passes.
    /// </summary>
    public class ProseRewriteHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProseRewriteHostedService> _logger;
        private readonly AiLaneGate _gate;

        public ProseRewriteHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<ProseRewriteHostedService> logger,
            AiLaneGate gate)
        {
            _gate = gate;
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
                _gate.Disabled(AiLanes.ProseRewrite, "Ai:BatchOllamaUrl not configured");
                return;
            }

            var model = _configuration["Ai:RewriteModel"] ?? _configuration["Ai:Model"] ?? "qwen2.5:3b";
            var interval = TimeSpan.FromMinutes(
                Math.Max(1, _configuration.GetValue("ProseRewrite:IntervalMinutes", 10)));
            var batchSize = Math.Clamp(_configuration.GetValue("ProseRewrite:BatchSize", 25), 1, 100);
            var retryDays = Math.Max(1, _configuration.GetValue("ProseRewrite:RetryDays", 30));
            var drain = AiLanes.ProseDrainWhileFull(_configuration);
            var drainPause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("ProseRewrite:PauseSeconds", 5)));
            var checkFidelity = _configuration.GetValue("ProseRewrite:CheckFidelity", true);

            _logger.LogInformation(
                "Prose rewrite enabled: {Url} ({Model}), every {Minutes}m, up to {Batch} recipes per sweep, fidelity check {Check}",
                baseUrl, model, interval.TotalMinutes, batchSize, checkFidelity ? "on" : "off");

            await SafeDelay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.ProseRewrite, stoppingToken)) return;
                var delay = interval;
                try
                {
                    var (seen, outage) = await SweepAsync(baseUrl, model, batchSize, retryDays, checkFidelity, stoppingToken);
                    if (outage != null)
                    {
                        _gate.Failed(AiLanes.ProseRewrite, outage);
                    }
                    else
                    {
                        _gate.Passed(AiLanes.ProseRewrite, seen, seen == 0);
                        if (drain && seen >= batchSize) delay = drainPause;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prose rewrite sweep failed");
                    _gate.Failed(AiLanes.ProseRewrite, ex);
                }

                await SafeDelay(delay, stoppingToken);
            }
        }

        internal async Task<(int Seen, Exception? Outage)> SweepAsync(string baseUrl, string model, int batchSize, int retryDays, bool checkFidelity, CancellationToken cancellationToken)
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
                return (0, null);
            }

            var ids = quarantined.Select(r => r.Id).ToList();
            var sources = (await db.ScrapedDocuments.AsNoTracking()
                    .Where(d => ids.Contains(d.RecipeId) && d.Format == ScrapedDocumentFormat.JsonLd)
                    .OrderBy(d => d.Id)
                    .Select(d => new { d.RecipeId, d.RawJsonLd })
                    .ToListAsync(cancellationToken))
                .GroupBy(d => d.RecipeId)
                .ToDictionary(g => g.Key, g => RewriteFidelity.Source(g.Last().RawJsonLd));

            var rewritten = 0;
            Exception? outage = null;
            foreach (var recipe in quarantined)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attemptedBefore = recipe.ProseRewriteAttemptedAt;
                recipe.ProseRewriteAttemptedAt = DateTime.UtcNow;

                var steps = recipe.RecipeSteps?.Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).ToList() ?? new();
                var source = sources.GetValueOrDefault(recipe.Id);
                var original = source != null && source.Steps.Count == steps.Count
                    ? new RewriteFidelity.SourceText(source.Description.Length > 0 ? source.Description : recipe.Description ?? string.Empty, source.Steps, recipe.Name ?? string.Empty)
                    : new RewriteFidelity.SourceText(recipe.Description ?? string.Empty, steps.Select(s => s.Description).ToList(), recipe.Name ?? string.Empty);
                try
                {
                    var answer = await RewriteAsync(baseUrl, model, recipe, original, steps.Count, null, cancellationToken);
                    var finding = answer == null || !checkFidelity ? null : Verify(Rewritten(recipe, answer), original, source);
                    if (finding is { Passed: false })
                    {
                        answer = await RewriteAsync(baseUrl, model, recipe, original, steps.Count, finding, cancellationToken);
                        finding = answer == null ? null : Verify(Rewritten(recipe, answer), original, source);
                    }

                    if (answer == null)
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }

                    if (finding is { Passed: false })
                    {
                        _logger.LogWarning("Rewrite for recipe {Id} failed the fidelity check ({Finding}); leaving quarantined", recipe.Id, finding);
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }

                    var rewrite = Rewritten(recipe, answer);
                    var description = rewrite.Description.Length > 0 ? rewrite.Description : recipe.Description;
                    var texts = rewrite.Steps.ToList();
                    var now = DateTime.UtcNow;
                    if (description != recipe.Description)
                    {
                        db.AuditLogEntries.Add(Journal(recipe.Id, nameof(recipe.Description), recipe.Description, $"[{model}] {description}", now));
                        recipe.Description = description;
                    }

                    if (steps.Count == 1 && texts.Count > 1)
                    {
                        db.AuditLogEntries.Add(Journal(recipe.Id, nameof(recipe.RecipeSteps), steps[0].Description, $"[{model}] {JsonSerializer.Serialize(texts)}", now));
                        RecipeStepSplit.Apply(recipe, steps[0], texts);
                    }
                    else
                    {
                        for (var i = 0; i < steps.Count; i++)
                        {
                            db.AuditLogEntries.Add(Journal(recipe.Id, $"RecipeStep:{steps[i].StepNumber}", steps[i].Description, $"[{model}] {texts[i]}", now));
                            steps[i].Description = texts[i];
                            steps[i].LastModifiedDate = now;
                            steps[i].LastModifiedByPersonId = SystemConstants.SystemPersonId;
                        }
                    }

                    db.AuditLogEntries.Add(Journal(recipe.Id, nameof(recipe.ContainsSourceProse), "true", $"[{model}] false", now));
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
                catch (HttpRequestException ex) when (AiLanes.IsOutage(ex))
                {
                    recipe.ProseRewriteAttemptedAt = attemptedBefore;
                    _logger.LogWarning(ex, "Model host unreachable while rewriting recipe {Id}; ending the sweep without recording an attempt", recipe.Id);
                    outage = ex;
                    break;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "Rewrite of recipe {Id} timed out; ending the sweep", recipe.Id);
                    await db.SaveChangesAsync(cancellationToken);
                    outage = ex;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Rewrite failed for recipe {Id}; leaving quarantined", recipe.Id);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            _logger.LogInformation("Prose rewrite sweep: {Rewritten}/{Seen} recipes cleared",
                rewritten, quarantined.Count);
            return (quarantined.Count, outage);
        }

        /// <summary>
        /// Asks the model once, and once more with the step count spelled out when its answer is off
        /// by one. A one-step original may come back as several steps, since old cookbooks wrote the
        /// whole method as one paragraph; anything else must keep the step count. With a failed
        /// fidelity check the model is told what its previous answer lost, added or copied.
        /// </summary>
        private async Task<RewritePayload?> RewriteAsync(string baseUrl, string model, Nom.Data.Recipe.RecipeEntity recipe,
            RewriteFidelity.SourceText original, int stepCount, RewriteFidelity.Finding? failed, CancellationToken cancellationToken)
        {
            var payload = new
            {
                name = recipe.Name,
                description = original.Description,
                steps = original.Steps,
            };
            var prompt =
                "Rewrite this recipe's description and steps in fresh, plain wording. " +
                "Preserve every fact — ingredients, amounts, temperatures, times, order, technique. " +
                "Invent nothing; drop storytelling and personal anecdotes. Keep the same NUMBER of steps, " +
                "one rewritten step per original step, same order. Reply with STRICT JSON: " +
                "{\"description\": str, \"steps\": [str, ...]} with exactly " +
                $"{stepCount} steps.\n\nRECIPE:\n{JsonSerializer.Serialize(payload)}"
                + (failed == null ? string.Empty : Feedback(failed));

            var answer = await GenerateAsync(baseUrl, model, prompt, cancellationToken);
            if (Acceptable(answer, stepCount)) return answer;

            var got = answer?.Steps?.Count ?? 0;
            if (answer?.Steps != null && Math.Abs(got - stepCount) == 1 && stepCount > 1)
            {
                answer = await GenerateAsync(baseUrl, model,
                    prompt + $"\n\nYour previous answer had {got} steps. Reply again with EXACTLY {stepCount} steps.", cancellationToken);
                if (Acceptable(answer, stepCount)) return answer;
                got = answer?.Steps?.Count ?? 0;
            }

            _logger.LogWarning("Rewrite for recipe {Id} returned {Got} steps for {Want}; leaving quarantined", recipe.Id, got, stepCount);
            return null;
        }

        private static string Feedback(RewriteFidelity.Finding failed)
        {
            var problems = new List<string>();
            if (failed.Lost.Count > 0) problems.Add($"it dropped these numbers the original states: {string.Join(", ", failed.Lost)}. Keep every amount, time and temperature exactly as the original gives it");
            if (failed.Added.Count > 0) problems.Add($"it stated numbers the original does not: {string.Join(", ", failed.Added)}. Add no number");
            if (failed.Copied != null) problems.Add($"it copied the original word for word: \"{failed.Copied}\". Reword it");
            if (failed.IsUnchanged) problems.Add("it repeated the original wording. Reword it");
            return $"\n\nYour previous answer was refused because {string.Join("; ", problems)}.";
        }

        private static RewriteFidelity.SourceText Rewritten(Nom.Data.Recipe.RecipeEntity recipe, RewritePayload answer) =>
            new(string.IsNullOrWhiteSpace(answer.Description) ? recipe.Description ?? string.Empty : answer.Description.Trim(),
                answer.Steps!.Select(s => s.Trim()).ToList());

        private static RewriteFidelity.Finding Verify(RewriteFidelity.SourceText rewrite, RewriteFidelity.SourceText original, RewriteFidelity.SourceText? source)
        {
            var finding = RewriteFidelity.Check(original, rewrite);
            return finding.Passed && source != null ? RewriteFidelity.Check(source, rewrite) : finding;
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
                options = new { temperature = 0, num_predict = 2048 },
            }, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return JsonSerializer.Deserialize<RewritePayload>(
                body.GetProperty("response").GetString() ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private static AuditLogEntryEntity Journal(long recipeId, string property, string? before, string after, DateTime at) => new()
        {
            EntityType = ProseRecheckService.RecipeEntityType,
            EntityId = recipeId,
            ChangeType = ProseRecheckService.RewriteChangeType,
            PropertyName = property,
            OldValue = ProseRecheckService.Clamp(before ?? string.Empty),
            NewValue = ProseRecheckService.Clamp(after),
            Timestamp = at,
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

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
