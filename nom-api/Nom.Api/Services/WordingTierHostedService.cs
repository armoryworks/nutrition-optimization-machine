using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// The deterministic wording tiers (plan C1 step 2, plan 3.3 step 10). Each pass takes in-use
    /// uncurated wordings with no row in the verified wording table, most lines first, and decides
    /// them by the tiers and the link gate, one lane-frame tier per wording tier of lane
    /// "wording-tiers". A tier writes its rows, through the verified wording loader and a
    /// batch-scoped relink, only while the frame holds it live; in shadow and while paused it runs
    /// the loader as a dry run and records. After each pass the auditor audits a sample of every
    /// tier that decided and the frame evaluates it, so a tier goes live, pauses and retracts its own
    /// rows by itself. Every pass passes the <see cref="AiLaneGate"/> (switch ai-pause-wording-tiers),
    /// so ai-pause-all and quiet hours hold it too.
    ///
    /// Off unless WordingTiers:Enabled=true. WordingTiers:BatchSize (default 100) wordings per pass;
    /// WordingTiers:MaxPerDay (default 300) writes per tier in 24 hours, and
    /// WordingTiers:HeadDefaultMaxPerDay (default 1) for the head-default tier, since one default
    /// moves every line of its wording; WordingTiers:PauseSeconds (default 5) between passes;
    /// WordingTiers:IdleMinutes (default 60) once nothing waits.
    /// </summary>
    public class WordingTierHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WordingTierHostedService> _logger;
        private readonly AiLaneGate _gate;

        public WordingTierHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<WordingTierHostedService> logger, AiLaneGate gate)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _gate = gate;
        }

        public static bool Enabled(IConfiguration configuration) => configuration.GetValue("WordingTiers:Enabled", false);

        /// <summary>The writes each tier may make in 24 hours.</summary>
        public static Dictionary<string, int> Caps(IConfiguration configuration)
        {
            var perTier = Math.Max(0, configuration.GetValue("WordingTiers:MaxPerDay", 300));
            var caps = WordingTiers.All.ToDictionary(t => t, _ => perTier);
            caps[WordingTiers.HeadDefault] = Math.Max(0, configuration.GetValue("WordingTiers:HeadDefaultMaxPerDay", 1));
            return caps;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled(_configuration))
            {
                _logger.LogInformation("Wording tier lane is off (WordingTiers:Enabled is not true)");
                _gate.Disabled(AiLanes.WordingTiers, "WordingTiers:Enabled is not true");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("WordingTiers:BatchSize", 100), 1, 1000);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("WordingTiers:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("WordingTiers:IdleMinutes", 60)));
            var caps = Caps(_configuration);
            var backoff = TimeSpan.FromMinutes(5);
            _logger.LogInformation("Wording tier lane on: {Batch} wordings per pass, at most {PerTier} writes per tier and {Head} head defaults in 24 h",
                batchSize, caps[WordingTiers.NameKey], caps[WordingTiers.HeadDefault]);

            if (!await Delay(TimeSpan.FromMinutes(5), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.WordingTiers, stoppingToken)) return;
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var lane = scope.ServiceProvider.GetRequiredService<IWordingTierLaneService>();
                    var modes = new Dictionary<string, string>();
                    var remaining = new Dictionary<string, int>();
                    foreach (var tier in WordingTiers.All)
                    {
                        modes[tier] = await _gate.WriteModeAsync(AiLanes.WordingTiers, tier, stoppingToken);
                        remaining[tier] = Math.Max(0, caps[tier] - await lane.WritesTodayAsync(tier, stoppingToken));
                    }

                    var batch = await lane.RunBatchAsync(modes, batchSize, remaining, cancellationToken: stoppingToken);
                    foreach (var tier in batch.Tiers.Where(t => WordingTiers.All.Contains(t)))
                    {
                        var verdict = await lane.AuditAndEvaluateAsync(tier, batch.FaultsByTier.TryGetValue(tier, out var faults) ? faults : new List<string>(), stoppingToken);
                        _logger.LogInformation("Wording tier {Tier}: {Mode} ({Reason})", tier, verdict.Mode, verdict.Reason);
                    }
                    _gate.Passed(AiLanes.WordingTiers, batch.Read, idle: batch.Read == 0);
                    if (batch.Read == 0) delay = idle;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Wording tier pass failed");
                    _gate.Failed(AiLanes.WordingTiers, ex);
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
