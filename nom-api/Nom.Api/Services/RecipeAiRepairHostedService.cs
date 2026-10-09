using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Works through vetting-flagged imports (RequiresRevision) in Id order with the local model:
    /// splits one-paragraph methods into steps and fills quantities written in the recipe, then
    /// re-vets. Small batches, one model request at a time and a pause between batches keep it
    /// polite to the other GPU lanes. Without an Ollama URL it only re-vets. One full pass per
    /// startup; after that it only picks up recipes newer than the last one it saw.
    ///
    /// Curation:AiRepair:Enabled=false disables it; Curation:AiRepair:BatchSize (default 5) recipes
    /// per batch; Curation:AiRepair:PauseSeconds (default 10) between batches;
    /// Curation:AiRepair:IdleMinutes (default 60) once a sweep reaches the end.
    /// </summary>
    public class RecipeAiRepairHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecipeAiRepairHostedService> _logger;

        public RecipeAiRepairHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<RecipeAiRepairHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Curation:AiRepair:Enabled", true))
            {
                _logger.LogInformation("AI recipe repair is disabled (Curation:AiRepair:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("Curation:AiRepair:BatchSize", 5), 1, 100);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("Curation:AiRepair:PauseSeconds", 10)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("Curation:AiRepair:IdleMinutes", 60)));
            var backoff = TimeSpan.FromMinutes(5);

            using (var scope = _scopeFactory.CreateScope())
            {
                var model = scope.ServiceProvider.GetRequiredService<IRecipeRepairModel>();
                _logger.LogInformation(model.IsConfigured
                    ? "AI recipe repair enabled: model {Model}, {Batch} recipes per batch"
                    : "AI recipe repair re-vetting only: no Ollama URL configured ({Model}, {Batch} per batch)", model.ModelName, batchSize);
            }

            if (!await Delay(TimeSpan.FromMinutes(4), stoppingToken)) return;

            long cursor = 0;
            int split = 0, filled = 0, cleared = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await AiQuietHours.WaitAsync(_configuration, _logger, stoppingToken)) return;
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repair = scope.ServiceProvider.GetRequiredService<IRecipeAiRepairService>();
                    var batch = await repair.RepairBatchAsync(cursor, batchSize, stoppingToken);
                    if (batch.Examined == 0)
                    {
                        if (split + filled + cleared > 0)
                        {
                            _logger.LogInformation("AI recipe repair sweep done: {Split} methods split, {Filled} quantities filled, {Cleared} recipes cleared vetting",
                                split, filled, cleared);
                        }
                        split = filled = cleared = 0;
                        delay = idle;
                    }
                    else
                    {
                        cursor = batch.LastRecipeId;
                        split += batch.StepsSplit;
                        filled += batch.QuantitiesFilled;
                        cleared += batch.Cleared;
                        if (batch.ModelCalls == 0) delay = TimeSpan.Zero;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI recipe repair batch after recipe {Cursor} failed", cursor);
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
