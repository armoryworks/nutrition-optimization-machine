using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// The model readers of the wording pipeline (plan C1 step 3, plan 3.3 step 10). Each pass takes
    /// the in-use uncurated wordings the deterministic tiers left, most recipes unblocked first, and
    /// has readers A and B read each blind, one lane-frame tier per kind of write of lane
    /// "wording-readers" (read, lexicon, alcohol). A tier writes, through the verified wording loader
    /// and a batch-scoped relink, only while the frame holds it live; in shadow and while paused it
    /// runs the loader as a dry run and records. After each pass the auditor audits a sample of every
    /// tier that decided and the frame evaluates it, so a tier goes live, pauses and retracts its own
    /// rows by itself. Every pass passes the <see cref="AiLaneGate"/> (switch ai-pause-wording-readers),
    /// so ai-pause-all and quiet hours hold it too.
    ///
    /// Off unless WordingReaders:Enabled=true. WordingReaders:BatchSize (default 60) wordings per pass;
    /// WordingReaders:MaxPerDay (default 300) food rows per tier in 24 hours and
    /// WordingReaders:LexiconMaxPerDay (default 30) lexicon entries; WordingReaders:PauseSeconds
    /// (default 5) between passes; WordingReaders:IdleMinutes (default 60) once nothing waits.
    /// </summary>
    public class WordingReaderHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WordingReaderHostedService> _logger;
        private readonly AiLaneGate _gate;

        public WordingReaderHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<WordingReaderHostedService> logger, AiLaneGate gate)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _gate = gate;
        }

        public static bool Enabled(IConfiguration configuration) => configuration.GetValue("WordingReaders:Enabled", false);

        /// <summary>The writes each tier may make in 24 hours.</summary>
        public static Dictionary<string, int> Caps(IConfiguration configuration)
        {
            var perTier = Math.Max(0, configuration.GetValue("WordingReaders:MaxPerDay", 300));
            var caps = WordingReaders.Tiers.ToDictionary(t => t, _ => perTier);
            caps[WordingReaders.Lexicon] = Math.Max(0, configuration.GetValue("WordingReaders:LexiconMaxPerDay", 30));
            return caps;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled(_configuration))
            {
                _logger.LogInformation("Wording reader lane is off (WordingReaders:Enabled is not true)");
                _gate.Disabled(AiLanes.WordingReaders, "WordingReaders:Enabled is not true");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("WordingReaders:BatchSize", 60), 1, 600);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("WordingReaders:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("WordingReaders:IdleMinutes", 60)));
            var caps = Caps(_configuration);
            var backoff = TimeSpan.FromMinutes(5);
            _logger.LogInformation("Wording reader lane on: {Batch} wordings per pass, at most {PerTier} food rows per tier and {Lexicon} lexicon entries in 24 h",
                batchSize, caps[WordingReaders.Read], caps[WordingReaders.Lexicon]);

            if (!await Delay(TimeSpan.FromMinutes(5), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.WordingReaders, stoppingToken)) return;
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var lane = scope.ServiceProvider.GetRequiredService<IWordingReaderLaneService>();
                    var modes = new Dictionary<string, string>();
                    var remaining = new Dictionary<string, int>();
                    foreach (var tier in WordingReaders.Tiers)
                    {
                        modes[tier] = await _gate.WriteModeAsync(AiLanes.WordingReaders, tier, stoppingToken);
                        remaining[tier] = Math.Max(0, caps[tier] - await lane.WritesTodayAsync(tier, stoppingToken));
                    }

                    var batch = await lane.RunBatchAsync(modes, batchSize, remaining, stoppingToken);
                    foreach (var tier in batch.Tiers.Where(t => WordingReaders.Tiers.Contains(t)))
                    {
                        var verdict = await lane.AuditAndEvaluateAsync(tier, batch.FaultsByTier.TryGetValue(tier, out var faults) ? faults : new List<string>(), stoppingToken);
                        _logger.LogInformation("Wording reader tier {Tier}: {Mode} ({Reason})", tier, verdict.Mode, verdict.Reason);
                    }
                    _gate.Passed(AiLanes.WordingReaders, batch.Read, idle: batch.Read == 0);
                    if (batch.Read == 0) delay = idle;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Wording reader pass failed");
                    _gate.Failed(AiLanes.WordingReaders, ex);
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
