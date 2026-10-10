using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Keeps the local model busy on unreviewed recipes when the GPU is otherwise free. Each round
    /// screens a small batch of public-domain recipes for non-food entries (cleaning, toiletry and
    /// remedy receipts; rejected only when the keyword pre-filter matches and the model says no),
    /// then classifies the unquantified ingredient lines of a small batch (reference, equipment,
    /// note, instruction and artefact lines stop counting as missing amounts) and re-vets them so
    /// clean recipes move to NonCurated for the auto-approval sweep. Batches are small and pauses
    /// short; Ollama's own queue interleaves this with the other lanes. A recipe the pre-filter
    /// flags is not line-classified until it has been screened. Sweeps restart from the beginning
    /// after an idle wait, and every recipe is attempted once per classifier or screening version.
    ///
    /// Curation:LineClassify:Enabled=false disables it; Curation:LineClassify:BatchSize (default 5)
    /// recipes per batch; Curation:LineClassify:PauseSeconds (default 3) between batches;
    /// Curation:LineClassify:IdleMinutes (default 60) once both sweeps reach the end;
    /// Curation:LineClassify:ScreenNonFood=false skips the non-food screening.
    /// </summary>
    public class RecipeLineClassifyHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecipeLineClassifyHostedService> _logger;

        public RecipeLineClassifyHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<RecipeLineClassifyHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Curation:LineClassify:Enabled", true))
            {
                _logger.LogInformation("Line classification is disabled (Curation:LineClassify:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("Curation:LineClassify:BatchSize", 5), 1, 100);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("Curation:LineClassify:PauseSeconds", 3)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("Curation:LineClassify:IdleMinutes", 60)));
            var screenNonFood = _configuration.GetValue("Curation:LineClassify:ScreenNonFood", true);
            var backoff = TimeSpan.FromMinutes(5);

            using (var scope = _scopeFactory.CreateScope())
            {
                var model = scope.ServiceProvider.GetRequiredService<IRecipeClassificationModel>();
                _logger.LogInformation(model.IsConfigured
                    ? "Line classification enabled: model {Model}, {Batch} recipes per batch"
                    : "Line classification deterministic only: no Ollama URL configured ({Model}, {Batch} per batch)", model.ModelName, batchSize);
            }

            if (!await Delay(TimeSpan.FromMinutes(3), stoppingToken)) return;

            long lineCursor = 0, screenCursor = 0;
            bool linesDone = false, screenDone = !screenNonFood;
            int classified = 0, cleared = 0, rejected = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await AiQuietHours.WaitAsync(_configuration, _logger, stoppingToken)) return;
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var calls = 0;
                    if (!screenDone)
                    {
                        var screening = scope.ServiceProvider.GetRequiredService<IRecipeNonFoodScreeningService>();
                        var batch = await screening.ScreenBatchAsync(screenCursor, batchSize, stoppingToken);
                        if (batch.Scanned == 0) screenDone = true;
                        else
                        {
                            screenCursor = batch.LastRecipeId;
                            rejected += batch.Rejected;
                            calls += batch.ModelCalls;
                        }
                    }

                    if (!linesDone)
                    {
                        var lines = scope.ServiceProvider.GetRequiredService<IRecipeLineClassificationService>();
                        var batch = await lines.ClassifyBatchAsync(lineCursor, batchSize, stoppingToken);
                        if (batch.Examined == 0) linesDone = true;
                        else
                        {
                            lineCursor = batch.LastRecipeId;
                            classified += batch.Deterministic + batch.ModelClassified;
                            cleared += batch.Cleared;
                            calls += batch.ModelCalls;
                        }
                    }

                    if (linesDone && screenDone)
                    {
                        if (classified + cleared + rejected > 0)
                        {
                            _logger.LogInformation("Line classification sweep done: {Classified} lines classified, {Cleared} recipes cleared vetting, {Rejected} non-food recipes rejected",
                                classified, cleared, rejected);
                        }
                        classified = cleared = rejected = 0;
                        lineCursor = screenCursor = 0;
                        linesDone = false;
                        screenDone = !screenNonFood;
                        delay = idle;
                    }
                    else if (calls == 0)
                    {
                        delay = TimeSpan.Zero;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Line classification batch after recipe {LineCursor} (screening after {ScreenCursor}) failed", lineCursor, screenCursor);
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
