using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// The reprocess lane (reprocess.md 2c, plan 3.3 step 6). After each start, and then every
    /// Reprocess:DetectEveryHours (default 24), the detector reads every recipe with a stored source
    /// that the current line rules have not read and stores what a rebuild would change, then marks
    /// the duplicates. Between sweeps the lane takes candidates, highest priority first, in two tiers on
    /// the lane frame: <c>rebuild</c> (at most Reprocess:MaxPerDay recipes in 24 hours, default 500)
    /// and <c>order</c>, the light order-only pass (Reprocess:OrderMaxPerDay, default 5000). A tier
    /// writes only while the frame holds it live; in shadow, and while paused, it previews and records.
    /// After each batch the line judge audits a sample and the frame evaluates the tier with the
    /// batch's rule-check faults, so the lane goes live, pauses and undoes its own writes by itself.
    /// Every batch passes the <see cref="AiLaneGate"/> (lane "reprocess", switch ai-pause-reprocess),
    /// so ai-pause-all and quiet hours hold it too.
    ///
    /// Off unless Reprocess:Enabled=true. Reprocess:BatchSize (default 100) recipes per batch;
    /// Reprocess:DetectBatchSize (default 200); Reprocess:PauseSeconds (default 5) between batches;
    /// Reprocess:IdleMinutes (default 30) once nothing waits.
    /// </summary>
    public class ReprocessHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReprocessHostedService> _logger;
        private readonly AiLaneGate _gate;

        public ReprocessHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ReprocessHostedService> logger, AiLaneGate gate)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _gate = gate;
        }

        public static bool Enabled(IConfiguration configuration) => configuration.GetValue("Reprocess:Enabled", false);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled(_configuration))
            {
                _logger.LogInformation("Reprocess lane is off (Reprocess:Enabled is not true)");
                _gate.Disabled(AiLanes.Reprocess, "Reprocess:Enabled is not true");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("Reprocess:BatchSize", 100), 1, Nom.Orch.Services.RecipeReprocessService.MaxRecipesPerCall);
            var detectBatch = Math.Clamp(_configuration.GetValue("Reprocess:DetectBatchSize", 200), 1, Nom.Orch.Services.RecipeReprocessService.MaxRecipesPerCall);
            var detectEvery = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("Reprocess:DetectEveryHours", 24)));
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("Reprocess:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("Reprocess:IdleMinutes", 30)));
            var caps = new Dictionary<string, int>
            {
                [ReprocessTier.Rebuild] = Math.Max(0, _configuration.GetValue("Reprocess:MaxPerDay", 500)),
                [ReprocessTier.Order] = Math.Max(0, _configuration.GetValue("Reprocess:OrderMaxPerDay", 5000)),
            };
            var backoff = TimeSpan.FromMinutes(5);
            _logger.LogInformation("Reprocess lane on: {Batch} recipes per batch, at most {Rebuild} rebuilds and {Order} order restores in 24 h, detector every {Hours} h",
                batchSize, caps[ReprocessTier.Rebuild], caps[ReprocessTier.Order], detectEvery.TotalHours);

            if (!await Delay(TimeSpan.FromMinutes(5), stoppingToken)) return;

            DateTime? lastSweep = null;
            long cursor = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.Reprocess, stoppingToken)) return;
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    if (lastSweep == null || DateTime.UtcNow - lastSweep >= detectEvery)
                    {
                        var detector = scope.ServiceProvider.GetRequiredService<IReprocessDetectorService>();
                        var found = await detector.DetectBatchAsync(cursor, detectBatch, stoppingToken);
                        _gate.Passed(AiLanes.Reprocess, found.Read, idle: false);
                        if (found.Read > 0)
                        {
                            cursor = found.LastRecipeId;
                            continue;
                        }
                        await detector.MarkDuplicatesAsync(stoppingToken);
                        _logger.LogInformation("Reprocess detector sweep done");
                        lastSweep = DateTime.UtcNow;
                        cursor = 0;
                    }

                    var lane = scope.ServiceProvider.GetRequiredService<IReprocessLaneService>();
                    var taken = 0;
                    foreach (var tier in new[] { ReprocessTier.Rebuild, ReprocessTier.Order })
                    {
                        if (await _gate.PausedAsync(AiLanes.Reprocess)) break;
                        var used = await lane.DecidedTodayAsync(tier, stoppingToken);
                        if (used >= caps[tier]) continue;
                        var mode = await _gate.WriteModeAsync(AiLanes.Reprocess, tier, stoppingToken);
                        var batch = await lane.RunBatchAsync(tier, mode, Math.Min(batchSize, caps[tier] - used), stoppingToken);
                        if (batch.Taken == 0) continue;
                        taken += batch.Taken;
                        var verdict = await lane.AuditAndEvaluateAsync(tier, batch.Faults, stoppingToken);
                        _logger.LogInformation("Reprocess lane {Tier}: {Mode} ({Reason})", tier, verdict.Mode, verdict.Reason);
                    }
                    _gate.Passed(AiLanes.Reprocess, taken, idle: taken == 0);
                    if (taken == 0) delay = idle;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reprocess lane round failed");
                    _gate.Failed(AiLanes.Reprocess, ex);
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
