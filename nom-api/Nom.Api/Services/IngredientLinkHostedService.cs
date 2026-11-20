using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

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
    /// IngredientLinking:AutoApplyNameMatches=true also applies exact name matches unreviewed, at most
    /// IngredientLinking:MaxNameMatchAppliesPerDay (default 200) a day and
    /// IngredientLinking:MaxNameMatchAppliesPerFoodPerDay (default 25) per USDA food.
    /// IngredientLinking:HoldGuard (default true) keeps a name match or model pick that a
    /// deterministic hold rule catches for a person instead of applying it.
    /// IngredientLinking:AutoApplyVerifiedAi=true triangulates model picks and applies verified ones (default off);
    /// IngredientLinking:VerifyPending=true (default false) checks pending picks in shadow, marking
    /// them verified or not without applying anything, whether or not auto-apply is on;
    /// IngredientLinking:VerifyMinConfidence (default 0.80) is the bar for checking a pick and
    /// IngredientLinking:VerifiedMinConfidence (default 0.85) the bar for applying a verified one;
    /// IngredientLinking:VerifyBatchSize (default 24) picks checked per pass;
    /// IngredientLinking:VerifyModel (no default) gives the second opinion; unset, or the same as the
    /// link model, means nothing is verified.
    /// IngredientLinking:Enabled=false disables it; IngredientLinking:BatchSize (default 30)
    /// ingredients per pass; IngredientLinking:PauseSeconds (default 5) between passes;
    /// IngredientLinking:IdleMinutes (default 30) once nothing is left to propose.
    /// Every pass passes the <see cref="AiLaneGate"/> (lane "link").
    /// </summary>
    public class IngredientLinkHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IngredientLinkHostedService> _logger;
        private readonly AiLaneGate _gate;

        private readonly HashSet<long> _unappliable = new();
        private bool _applyNameMatches;
        private bool _holdGuard = true;
        private decimal _applyMinConfidence = 0.85m;

        public IngredientLinkHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<IngredientLinkHostedService> logger,
            AiLaneGate gate)
        {
            _gate = gate;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("IngredientLinking:Enabled", true))
            {
                _logger.LogInformation("Ingredient linking is disabled (IngredientLinking:Enabled=false)");
                _gate.Disabled(AiLanes.Link, "IngredientLinking:Enabled=false");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("IngredientLinking:BatchSize", 30), 1, 200);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("IngredientLinking:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("IngredientLinking:IdleMinutes", 30)));
            var backoff = TimeSpan.FromMinutes(5);
            bool verifyUsable;

            using (var scope = _scopeFactory.CreateScope())
            {
                var matcher = scope.ServiceProvider.GetRequiredService<IIngredientLinkMatcher>();
                if (!matcher.IsConfigured)
                {
                    _logger.LogInformation("Ingredient linking idle: no Ollama URL configured");
                    _gate.Disabled(AiLanes.Link, "no Ollama URL configured");
                    return;
                }
                _logger.LogInformation("Ingredient linking enabled: model {Model}, {Batch} ingredients per pass", matcher.ModelName, batchSize);
                verifyUsable = SecondOpinionUsable(matcher, scope.ServiceProvider.GetRequiredService<IIngredientLinkVerifier>());
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
            _applyNameMatches = _configuration.GetValue("IngredientLinking:AutoApplyNameMatches", false);
            _holdGuard = _configuration.GetValue("IngredientLinking:HoldGuard", true);
            if (autoApply) await ApplyCertainLinksAsync(stoppingToken);
            var autoApplyVerified = _configuration.GetValue("IngredientLinking:AutoApplyVerifiedAi", false);
            _applyMinConfidence = Math.Clamp(_configuration.GetValue("IngredientLinking:VerifiedMinConfidence", 0.85m), 0m, 1m);
            var minConfidence = Math.Clamp(_configuration.GetValue("IngredientLinking:VerifyMinConfidence", 0.80m), 0m, _applyMinConfidence);
            var verifyBatch = Math.Clamp(_configuration.GetValue("IngredientLinking:VerifyBatchSize", 24), 1, 200);
            if (autoApplyVerified) await ApplyVerifiedLinksAsync(stoppingToken);
            var verify = verifyUsable && (autoApplyVerified || _configuration.GetValue("IngredientLinking:VerifyPending", false));

            int proposed = 0, sinceLog = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.Link, stoppingToken)) return;
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
                            _gate.Failed(AiLanes.Link, new HttpRequestException("the link model did not answer"));
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

                    if (verify && delay != backoff)
                    {
                        var verified = await links.VerifyPendingAiAsync(verifyBatch, minConfidence, stoppingToken);
                        if (verified == null)
                        {
                            delay = backoff;
                            _gate.Failed(AiLanes.Link, new HttpRequestException("the link verifier did not answer"));
                        }
                        else
                        {
                            if (autoApplyVerified && verified.Verified > 0) await ApplyVerifiedLinksAsync(stoppingToken);
                            if (verified.MoreWaiting && sources.Count == 0) delay = pause;
                        }
                    }
                    if (delay != backoff) _gate.Passed(AiLanes.Link, sources.Count, delay == idle);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ingredient linking pass failed");
                    _gate.Failed(AiLanes.Link, ex);
                    delay = backoff;
                }

                if (!await Delay(delay, stoppingToken)) return;
            }
        }

        /// <summary>
        /// The second opinion runs only with a judge model of its own: none configured means no
        /// checking, and the picker's own model judging its picks is refused outright.
        /// </summary>
        private bool SecondOpinionUsable(IIngredientLinkMatcher matcher, IIngredientLinkVerifier verifier)
        {
            if (!verifier.IsConfigured)
            {
                _logger.LogInformation("Ingredient link second opinion off: IngredientLinking:VerifyModel is not set, so no pick is verified");
                return false;
            }
            if (string.Equals(verifier.ModelName, matcher.ModelName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Ingredient link second opinion refused: IngredientLinking:VerifyModel {Model} is the picker's own model; no pick is verified", verifier.ModelName);
                return false;
            }
            _logger.LogInformation("Ingredient link second opinion: {Model} prompt {Version}", verifier.ModelName, verifier.PromptVersion);
            return true;
        }

        private async Task ApplyCertainLinksAsync(CancellationToken stoppingToken)
        {
            await ApplyLinksAsync((links, skip, token) => links.PendingDeterministicAsync(50, skip, false, token),
                "certain links (staples, same-name USDA attaches)", stoppingToken);
            if (_applyNameMatches) await ApplyNameMatchesAsync(stoppingToken);
        }

        /// <summary>
        /// Exact name matches are applied under their own daily allowance and per-food share
        /// (IngredientLinking:MaxNameMatchAppliesPerDay, default 200, and
        /// IngredientLinking:MaxNameMatchAppliesPerFoodPerDay, default 25), and only once the hold
        /// rules have let them through. A cap of 0 stops them without touching staples.
        /// </summary>
        private Task ApplyNameMatchesAsync(CancellationToken stoppingToken)
        {
            var dailyCap = Math.Max(0, _configuration.GetValue("IngredientLinking:MaxNameMatchAppliesPerDay", 200));
            var perFoodCap = Math.Max(1, _configuration.GetValue("IngredientLinking:MaxNameMatchAppliesPerFoodPerDay", 25));
            return ApplyLinksCoreAsync(async (services, skip, token) =>
            {
                var gate = services.GetRequiredService<ILinkAutoApplyService>();
                var allowance = await gate.AllowanceAsync(Nom.Orch.Services.IngredientLinkService.ExactSource, dailyCap, perFoodCap, token);
                if (allowance.BusyTargets.Count > 0)
                {
                    _logger.LogWarning("Ingredient linking: {Count} USDA foods reached {Cap} applied name matches in a day and are skipped until tomorrow: {Foods}",
                        allowance.BusyTargets.Count, perFoodCap, string.Join(", ", allowance.BusyTargets.Take(20)));
                }
                if (allowance.Remaining == 0)
                {
                    if (dailyCap > 0) _logger.LogWarning("Ingredient linking: the daily allowance of {Cap} applied name matches is used up; the rest wait", dailyCap);
                    return Array.Empty<long>();
                }
                return await gate.PendingNameMatchesAsync(Math.Min(50, allowance.Remaining), skip, allowance.BusyTargets, token);
            }, "exact name matches", stoppingToken, guarded: true);
        }

        /// <summary>
        /// Model picks are applied under a daily allowance and a per-food share of it
        /// (IngredientLinking:MaxVerifiedAppliesPerDay, default 500, and
        /// IngredientLinking:MaxVerifiedAppliesPerFoodPerDay, default 25). Reaching either is logged
        /// as a warning: it is the sign of a matcher fault, not of a productive day.
        /// </summary>
        private Task ApplyVerifiedLinksAsync(CancellationToken stoppingToken)
        {
            var dailyCap = Math.Max(0, _configuration.GetValue("IngredientLinking:MaxVerifiedAppliesPerDay", 500));
            var perFoodCap = Math.Max(1, _configuration.GetValue("IngredientLinking:MaxVerifiedAppliesPerFoodPerDay", 25));
            return ApplyLinksAsync(async (links, skip, token) =>
            {
                var allowance = await links.VerifiedAllowanceAsync(dailyCap, perFoodCap, token);
                if (allowance.BusyTargets.Count > 0)
                {
                    _logger.LogWarning("Ingredient linking: {Count} USDA foods reached {Cap} applied model picks in a day and are skipped until tomorrow: {Foods}",
                        allowance.BusyTargets.Count, perFoodCap, string.Join(", ", allowance.BusyTargets.Take(20)));
                }
                if (allowance.Remaining == 0)
                {
                    _logger.LogWarning("Ingredient linking: the daily allowance of {Cap} applied model picks is used up; the rest wait", dailyCap);
                    return Array.Empty<long>();
                }
                return await links.PendingVerifiedAsync(Math.Min(50, allowance.Remaining), skip, _applyMinConfidence, token, allowance.BusyTargets);
            }, "model picks verified by triangulation", stoppingToken, guarded: true);
        }

        private Task ApplyLinksAsync(
            Func<IIngredientLinkService, IReadOnlyCollection<long>, CancellationToken, Task<IReadOnlyList<long>>> next,
            string what,
            CancellationToken stoppingToken,
            bool guarded = false) =>
            ApplyLinksCoreAsync((services, skip, token) => next(services.GetRequiredService<IIngredientLinkService>(), skip, token), what, stoppingToken, guarded);

        /// <summary>
        /// Applies what <paramref name="next"/> hands out until it hands out nothing. With
        /// <paramref name="guarded"/> (and IngredientLinking:HoldGuard on) each batch first goes
        /// through the hold rules; a held pick is marked for a person and not asked for again.
        /// </summary>
        private async Task ApplyLinksCoreAsync(
            Func<IServiceProvider, IReadOnlyCollection<long>, CancellationToken, Task<IReadOnlyList<long>>> next,
            string what,
            CancellationToken stoppingToken,
            bool guarded)
        {
            var applied = 0;
            var held = 0;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var ids = await next(scope.ServiceProvider, _unappliable, stoppingToken);
                    if (ids.Count == 0) break;
                    if (guarded && _holdGuard)
                    {
                        var screened = await scope.ServiceProvider.GetRequiredService<ILinkAutoApplyService>().ScreenAsync(ids, stoppingToken);
                        foreach (var id in screened.Held) _unappliable.Add(id);
                        held += screened.Held.Count;
                        ids = screened.Allowed;
                        if (ids.Count == 0) continue;
                    }
                    var review = scope.ServiceProvider.GetRequiredService<IFoodCatalogReviewService>();
                    foreach (var id in ids)
                    {
                        try
                        {
                            if (await review.ApplyProposalAsync(id, Nom.Data.SystemConstants.SystemPersonId)) applied++;
                            else _unappliable.Add(id);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _unappliable.Add(id);
                            _logger.LogWarning(ex, "Applying proposal {Id} of the {What} failed; it is left for a person", id, what);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Applying {What} failed", what);
            }
            if (applied > 0) _logger.LogInformation("Ingredient linking: applied {Count} {What}", applied, what);
            if (held > 0) _logger.LogInformation("Ingredient linking: held {Count} {What} for a person", held, what);
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
