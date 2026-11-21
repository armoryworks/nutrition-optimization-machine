using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Re-checks the shown prose of every scraped recipe with the prose check and withholds the
    /// prose of those that fail (<see cref="IProseRecheckService.RecheckAsync"/> with apply), so
    /// copied or changed text that an older lane or check let through stops being served without a
    /// person. A full run is due when none is recorded, when the prose check or the stock-phrase
    /// index has a version the last run did not use (so a deploy with a new check runs it), and
    /// otherwise every ProseRecheck:IntervalDays (default 7); it never runs while no stock-phrase
    /// index has been built. Deterministic, no model.
    ///
    /// ProseRecheck:Enabled (default false) turns it on, after the first withhold has been applied by
    /// hand; ProseRecheck:CheckMinutes (default 60) is how often it looks whether a run is due;
    /// ProseRecheck:StartDelaySeconds (default 300) is the wait after start. The platform feature
    /// pause-prose-recheck holds it; turn that on before releasing withheld recipes with the
    /// re-check undo, or the next run withholds them again.
    /// </summary>
    public class ProseRecheckHostedService : BackgroundService
    {
        private const int MaxRecipesPerRun = 100000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProseRecheckHostedService> _logger;

        public ProseRecheckHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ProseRecheckHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("ProseRecheck:Enabled", false))
            {
                _logger.LogInformation("Prose re-check job is off (ProseRecheck:Enabled=false)");
                return;
            }

            var interval = TimeSpan.FromDays(Math.Max(1, _configuration.GetValue("ProseRecheck:IntervalDays", 7)));
            var check = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("ProseRecheck:CheckMinutes", 60)));
            var startDelay = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("ProseRecheck:StartDelaySeconds", 300)));

            if (!await Delay(startDelay, stoppingToken)) return;
            _logger.LogInformation("Prose re-check job on (a full run every {Days} days, or when the check or stock index changes)", interval.TotalDays);

            var paused = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    paused = await RunOnceAsync(interval, paused, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prose re-check run failed; it is tried again at the next check");
                }

                if (!await Delay(check, stoppingToken)) return;
            }
        }

        /// <summary>One look: runs a full re-check when one is due and the job is not paused. Returns whether it was paused.</summary>
        public async Task<bool> RunOnceAsync(TimeSpan interval, bool wasPaused, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var features = scope.ServiceProvider.GetRequiredService<IPlatformFeatureService>();
            if (await features.IsEnabledAsync(IPlatformFeatureService.Keys.ProseRecheckPause))
            {
                if (!wasPaused) _logger.LogInformation("Prose re-check paused (platform feature {Key})", IPlatformFeatureService.Keys.ProseRecheckPause);
                return true;
            }

            var recheck = scope.ServiceProvider.GetRequiredService<IProseRecheckService>();
            var due = await recheck.DueAsync(interval, DateTime.UtcNow, cancellationToken);
            if (due == null) return false;

            _logger.LogInformation("Prose re-check starting ({Reason})", due);
            var result = await recheck.RecheckAsync(apply: true, maxRecipes: MaxRecipesPerRun, sampleSize: 0, cancellationToken: cancellationToken,
                hold: _ => features.IsEnabledAsync(IPlatformFeatureService.Keys.ProseRecheckPause));
            if (result.Refused != null)
            {
                _logger.LogWarning("Prose re-check refused: {Reason}", result.Refused);
                return false;
            }

            if (result.Held)
            {
                _logger.LogInformation("Prose re-check paused (platform feature {Key}) after withholding {Withheld}; {Remaining} failures left shown and no run recorded",
                    IPlatformFeatureService.Keys.ProseRecheckPause, result.Withheld, result.Remaining);
                return true;
            }

            if (result.Errored > 0)
            {
                _logger.LogWarning("Prose re-check could not check {Errored} recipe(s), left as they are: {Errors}",
                    result.Errored, string.Join("; ", result.Errors.Take(20).Select(e => $"#{e.RecipeId} {e.Error}")));
            }

            _logger.LogInformation(
                "Prose re-check done (check {Check}, stock index {Stock}): {Examined} examined, {Passed} passed, {Withheld} withheld ({Approved} approved), {Edited} skipped as edited by a person, {NoSource} without a source; by reason {Reasons}",
                result.CheckVersion, result.StockVersion, result.Examined, result.Passed, result.Withheld, result.FailedApproved,
                result.SkippedEditedByPerson, result.SkippedNoSource,
                string.Join(", ", result.FailedByReason.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key} {r.Value}")));
            return false;
        }

        private static async Task<bool> Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
