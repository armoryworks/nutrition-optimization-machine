using Nom.Data;
using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Sweeps NonCurated recipes in Id order and approves those meeting the auto-approval rules
    /// (public domain, or rewritten scrape with an image of our own; vetting clean; every ingredient
    /// curated). Ingredients are curated continuously by other lanes, so once a sweep reaches the
    /// end it waits and starts over, picking up recipes whose last ingredient has since been curated.
    ///
    /// Curation:AutoApprove:Enabled=false disables it; Curation:AutoApprove:BatchSize (default 200)
    /// recipes per batch; Curation:AutoApprove:PauseMilliseconds (default 500) between batches;
    /// Curation:AutoApprove:IdleMinutes (default 30) between sweeps. Every sweep logs what it
    /// examined and why candidates were refused, approved or not. A sweep is skipped, with a
    /// one-line heartbeat, when nothing it decides on has changed since the last full one
    /// (<see cref="AutoApproveChangeSignal"/>; Curation:AutoApprove:SkipUnchanged, default true),
    /// but a full sweep still runs at least every Curation:AutoApprove:FullSweepHours (default 24).
    /// The platform feature pause-auto-approve holds it before its next batch, within about 30
    /// seconds, for a data window whose changes may still be undone; it then resumes where it
    /// left off. Curation:AutoApprove:StartDelaySeconds (default 120) is the wait after start.
    /// </summary>
    public class RecipeAutoApproveHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecipeAutoApproveHostedService> _logger;

        public RecipeAutoApproveHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<RecipeAutoApproveHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Curation:AutoApprove:Enabled", true))
            {
                _logger.LogInformation("Recipe auto-approval is disabled (Curation:AutoApprove:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("Curation:AutoApprove:BatchSize", 200), 1, 2000);
            var pause = TimeSpan.FromMilliseconds(Math.Max(0, _configuration.GetValue("Curation:AutoApprove:PauseMilliseconds", 500)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("Curation:AutoApprove:IdleMinutes", 30)));
            var backoff = TimeSpan.FromMinutes(5);
            var skipUnchanged = _configuration.GetValue("Curation:AutoApprove:SkipUnchanged", true);
            var fullSweepEvery = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("Curation:AutoApprove:FullSweepHours", 24)));
            var startDelay = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("Curation:AutoApprove:StartDelaySeconds", 120)));
            var pauseCheck = TimeSpan.FromSeconds(Math.Max(1, _configuration.GetValue("Curation:AutoApprove:PauseCheckSeconds", 30)));

            if (!await Delay(startDelay, stoppingToken)) return;
            _logger.LogInformation("Recipe auto-approval enabled ({Batch} recipes per batch, sweeping every {Idle}m)", batchSize, idle.TotalMinutes);

            long cursor = 0;
            int approved = 0, examined = 0;
            var refusals = new Dictionary<string, int>();
            string? lastFingerprint = null;
            DateTime lastFullSweep = DateTime.MinValue;
            string lastOutcome = string.Empty;
            var paused = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    if (await scope.ServiceProvider.GetRequiredService<IPlatformFeatureService>().IsEnabledAsync(IPlatformFeatureService.Keys.AutoApprovePause))
                    {
                        if (!paused) _logger.LogInformation("Recipe auto-approval paused (platform feature {Key}); it resumes after recipe {Cursor} when that is turned off",
                            IPlatformFeatureService.Keys.AutoApprovePause, cursor);
                        paused = true;
                        if (!await Delay(pauseCheck, stoppingToken)) return;
                        continue;
                    }
                    if (paused) _logger.LogInformation("Recipe auto-approval resumed after recipe {Cursor}", cursor);
                    paused = false;
                    if (cursor == 0 && skipUnchanged)
                    {
                        var fingerprint = await AutoApproveChangeSignal.FingerprintAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), stoppingToken);
                        if (fingerprint == lastFingerprint && DateTime.UtcNow - lastFullSweep < fullSweepEvery)
                        {
                            _logger.LogInformation("Recipe auto-approval: nothing changed since the sweep at {At:u} ({Outcome}); next full sweep by {Due:u}",
                                lastFullSweep, lastOutcome, lastFullSweep + fullSweepEvery);
                            if (!await Delay(idle, stoppingToken)) return;
                            continue;
                        }
                        lastFingerprint = fingerprint;
                        lastFullSweep = DateTime.UtcNow;
                    }
                    var curation = scope.ServiceProvider.GetRequiredService<IRecipeAutoCurationService>();
                    var batch = await curation.AutoApproveBatchAsync(cursor, batchSize, stoppingToken);
                    if (batch.Examined == 0)
                    {
                        lastOutcome = $"{examined} examined, {approved} approved";
                        if (examined > 0)
                        {
                            _logger.LogInformation("Recipe auto-approval sweep done: {Examined} examined, {Approved} approved, refused by rule: {Refusals}",
                                examined, approved, string.Join(", ", refusals.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {r.Value}")));
                        }
                        approved = 0;
                        examined = 0;
                        refusals.Clear();
                        cursor = 0;
                        delay = idle;
                    }
                    else
                    {
                        approved += batch.Approved;
                        examined += batch.Examined;
                        foreach (var (key, count) in batch.Refusals ?? new Dictionary<string, int>()) refusals[key] = refusals.GetValueOrDefault(key) + count;
                        cursor = batch.LastRecipeId;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recipe auto-approval batch after recipe {Cursor} failed", cursor);
                    lastFingerprint = null;
                    delay = backoff;
                }

                if (!await Delay(delay, stoppingToken)) return;
            }
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
