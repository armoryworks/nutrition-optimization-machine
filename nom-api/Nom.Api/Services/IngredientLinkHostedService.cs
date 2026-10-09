using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Proposes USDA links for catalog ingredients recipes use, most-used first, as food-catalog
    /// proposals. Certain links (staples, same-name attaches) are applied straight away as the System
    /// person; name-matcher picks wait for an admin. Between proposal passes, pending model picks
    /// are triangulated (model confident, name matcher ranks the same food first, a second model call
    /// names it too) and applied as the System person only when all three agree; the rest wait for
    /// an admin. Runs only when an Ollama URL is configured, because ambiguous names need the model
    /// to choose.
    ///
    /// IngredientLinking:AutoApplyCertain=false leaves certain links for an admin too.
    /// IngredientLinking:AutoApplyVerifiedAi=false stops triangulating model picks;
    /// IngredientLinking:VerifiedMinConfidence (default 0.85) is the confidence bar;
    /// IngredientLinking:VerifyBatchSize (default 24) picks checked per pass;
    /// IngredientLinking:VerifyModel (default the link model) gives the second opinion.
    /// IngredientLinking:Enabled=false disables it; IngredientLinking:BatchSize (default 30)
    /// ingredients per pass; IngredientLinking:PauseSeconds (default 5) between passes;
    /// IngredientLinking:IdleMinutes (default 30) once nothing is left to propose.
    /// </summary>
    public class IngredientLinkHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IngredientLinkHostedService> _logger;

        private readonly HashSet<long> _unappliable = new();

        public IngredientLinkHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<IngredientLinkHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("IngredientLinking:Enabled", true))
            {
                _logger.LogInformation("Ingredient linking is disabled (IngredientLinking:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("IngredientLinking:BatchSize", 30), 1, 200);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("IngredientLinking:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("IngredientLinking:IdleMinutes", 30)));
            var backoff = TimeSpan.FromMinutes(5);

            using (var scope = _scopeFactory.CreateScope())
            {
                var matcher = scope.ServiceProvider.GetRequiredService<IIngredientLinkMatcher>();
                if (!matcher.IsConfigured)
                {
                    _logger.LogInformation("Ingredient linking idle: no Ollama URL configured");
                    return;
                }
                _logger.LogInformation("Ingredient linking enabled: model {Model}, {Batch} ingredients per pass", matcher.ModelName, batchSize);
            }

            if (!await Delay(TimeSpan.FromMinutes(2), stoppingToken)) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var corrected = await scope.ServiceProvider.GetRequiredService<IIngredientLinkService>().ApplyStapleDefaultsToPendingAsync(stoppingToken);
                if (corrected > 0) _logger.LogInformation("Ingredient linking: {Count} pending staple links re-pointed to their standard USDA entry", corrected);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Re-pointing pending staple links failed");
            }
            var autoApply = _configuration.GetValue("IngredientLinking:AutoApplyCertain", true);
            if (autoApply) await ApplyCertainLinksAsync(stoppingToken);
            var autoApplyVerified = _configuration.GetValue("IngredientLinking:AutoApplyVerifiedAi", true);
            var minConfidence = Math.Clamp(_configuration.GetValue("IngredientLinking:VerifiedMinConfidence", 0.85m), 0m, 1m);
            var verifyBatch = Math.Clamp(_configuration.GetValue("IngredientLinking:VerifyBatchSize", 24), 1, 200);
            if (autoApplyVerified) await ApplyVerifiedLinksAsync(stoppingToken);

            int proposed = 0, sinceLog = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var links = scope.ServiceProvider.GetRequiredService<IIngredientLinkService>();
                    var sources = await links.NextSourcesAsync(batchSize, stoppingToken);
                    if (sources.Count == 0)
                    {
                        if (proposed > 0) _logger.LogInformation("Ingredient linking caught up ({Count} ingredients this run)", proposed);
                        proposed = 0;
                        delay = idle;
                    }
                    else
                    {
                        var result = await links.ProposeAsync(sources, stoppingToken);
                        if (result == null)
                        {
                            delay = backoff;
                        }
                        else
                        {
                            if (autoApply && result.ExactProposals > 0) await ApplyCertainLinksAsync(stoppingToken);
                            proposed += result.Seen;
                            sinceLog += result.Seen;
                            if (sinceLog >= 1000)
                            {
                                _logger.LogInformation("Ingredient linking: {Count} more ingredients reviewed", sinceLog);
                                sinceLog = 0;
                            }
                        }
                    }

                    if (autoApplyVerified && delay != backoff)
                    {
                        var verified = await links.VerifyPendingAiAsync(verifyBatch, minConfidence, stoppingToken);
                        if (verified == null)
                        {
                            delay = backoff;
                        }
                        else
                        {
                            if (verified.Verified > 0) await ApplyVerifiedLinksAsync(stoppingToken);
                            if (verified.MoreWaiting && sources.Count == 0) delay = pause;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ingredient linking pass failed");
                    delay = backoff;
                }

                if (!await Delay(delay, stoppingToken)) return;
            }
        }

        private Task ApplyCertainLinksAsync(CancellationToken stoppingToken) =>
            ApplyLinksAsync((links, skip, token) => links.PendingDeterministicAsync(50, skip, token), "certain links (staples, same-name USDA attaches)", stoppingToken);

        private Task ApplyVerifiedLinksAsync(CancellationToken stoppingToken) =>
            ApplyLinksAsync((links, skip, token) => links.PendingVerifiedAsync(50, skip, token), "model picks verified by triangulation", stoppingToken);

        private async Task ApplyLinksAsync(
            Func<IIngredientLinkService, IReadOnlyCollection<long>, CancellationToken, Task<IReadOnlyList<long>>> next,
            string what,
            CancellationToken stoppingToken)
        {
            var applied = 0;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var ids = await next(scope.ServiceProvider.GetRequiredService<IIngredientLinkService>(), _unappliable, stoppingToken);
                    if (ids.Count == 0) break;
                    var review = scope.ServiceProvider.GetRequiredService<IFoodCatalogReviewService>();
                    foreach (var id in ids)
                    {
                        if (await review.ApplyProposalAsync(id, Nom.Data.SystemConstants.SystemPersonId)) applied++;
                        else _unappliable.Add(id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Applying {What} failed", what);
            }
            if (applied > 0) _logger.LogInformation("Ingredient linking: applied {Count} {What}", applied, what);
        }

        private static async Task<bool> Delay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
