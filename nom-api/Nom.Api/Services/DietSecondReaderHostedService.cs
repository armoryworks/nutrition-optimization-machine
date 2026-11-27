using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// Runs the second diet reader's shadow lane (<see cref="DietSecondReaderLaneService"/>, lane diet-second-reader): each
    /// pass reads the next recipes of its scope (approved first, then waiting for approval) that a model of the pair has
    /// not read under the current prompt and text, and stores the readings. It writes nothing a household sees; the stored
    /// gate reads the readings only while the platform setting diet-second-reader-gate is on. Every pass passes the
    /// <see cref="AiLaneGate"/> (switch ai-pause-diet-second-reader, ai-pause-all, quiet hours, the host breaker), and the
    /// pause switch is read again before each recipe.
    ///
    /// Off unless DietSecondReader:Enabled=true. DietSecondReader:BatchSize (default 10) recipes per pass;
    /// DietSecondReader:PauseSeconds (default 5) between passes; DietSecondReader:IdleMinutes (default 60) once every
    /// recipe of the scope is read.
    /// </summary>
    public class DietSecondReaderHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DietSecondReaderHostedService> _logger;
        private readonly AiLaneGate _gate;

        public DietSecondReaderHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<DietSecondReaderHostedService> logger, AiLaneGate gate)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _gate = gate;
        }

        public static bool Enabled(IConfiguration configuration) => configuration.GetValue("DietSecondReader:Enabled", false);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled(_configuration))
            {
                _logger.LogInformation("Second diet reader lane is off (DietSecondReader:Enabled is not true)");
                _gate.Disabled(AiLanes.DietSecondReader, "DietSecondReader:Enabled is not true");
                return;
            }
            if (string.IsNullOrWhiteSpace(DietSecondReaderLaneService.Host(_configuration)))
            {
                _logger.LogWarning("Second diet reader lane has no model host (DietSecondReader:OllamaUrl, Ai:BatchOllamaUrl or Ai:OllamaUrl)");
                _gate.Disabled(AiLanes.DietSecondReader, "no model host configured");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("DietSecondReader:BatchSize", 10), 1, 200);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("DietSecondReader:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("DietSecondReader:IdleMinutes", 60)));
            var backoff = TimeSpan.FromMinutes(5);
            _logger.LogInformation("Second diet reader lane on: {Batch} recipes per pass with {Pair}", batchSize, string.Join(" and ", DietSecondGate.Pair));

            if (!await Delay(TimeSpan.FromMinutes(5), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.DietSecondReader, stoppingToken)) return;
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var lane = scope.ServiceProvider.GetRequiredService<DietSecondReaderLaneService>();
                    var batch = await lane.RunBatchAsync(batchSize, () => _gate.PausedAsync(AiLanes.DietSecondReader), stoppingToken);
                    if (batch.Recipes > 0)
                        _logger.LogInformation("Second diet reader: read {Recipes} recipes, {Readings} readings, {Unreadable} unreadable", batch.Recipes, batch.Readings, batch.Unreadable);
                    _gate.Passed(AiLanes.DietSecondReader, batch.Recipes, idle: batch.Recipes == 0);
                    if (batch.Recipes == 0) delay = idle;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Second diet reader pass failed");
                    _gate.Failed(AiLanes.DietSecondReader, ex);
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
