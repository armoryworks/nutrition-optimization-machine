using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// One pass per startup over every scraped recipe: rebuilds ingredient quantities lost at
    /// import from each row's RawLine and re-vets recipes no human has reviewed. Idempotent —
    /// rows that already carry a quantity and recipes already reviewed are left alone.
    ///
    /// ScrapedRepair:Enabled=false disables it; ScrapedRepair:BatchSize (default 500) recipes
    /// per batch; ScrapedRepair:PauseMilliseconds (default 250) between batches.
    /// </summary>
    public class ScrapedRecipeRepairHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ScrapedRecipeRepairHostedService> _logger;

        public ScrapedRecipeRepairHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ScrapedRecipeRepairHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("ScrapedRepair:Enabled", true))
            {
                _logger.LogInformation("Scraped recipe repair is disabled (ScrapedRepair:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("ScrapedRepair:BatchSize", 500), 1, 5000);
            var pause = TimeSpan.FromMilliseconds(Math.Max(0, _configuration.GetValue("ScrapedRepair:PauseMilliseconds", 250)));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            long cursor = 0;
            long recipes = 0, repaired = 0, unparsed = 0, revetted = 0, cleared = 0;
            var started = DateTime.UtcNow;
            _logger.LogInformation("Scraped recipe repair starting ({Batch} recipes per batch)", batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repair = scope.ServiceProvider.GetRequiredService<IScrapedRecipeRepairService>();
                    var batch = await repair.RepairBatchAsync(cursor, batchSize, stoppingToken);
                    if (batch.Recipes == 0) break;

                    cursor = batch.LastRecipeId;
                    recipes += batch.Recipes;
                    repaired += batch.RowsRepaired;
                    unparsed += batch.RowsStillUnparsed;
                    revetted += batch.Revetted;
                    cleared += batch.Cleared;

                    if (recipes % (batchSize * 20) < batch.Recipes)
                    {
                        _logger.LogInformation("Scraped recipe repair: {Recipes} recipes, {Repaired} rows repaired, {Cleared} cleared vetting so far",
                            recipes, repaired, cleared);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scraped recipe repair batch after recipe {Cursor} failed; stopping this pass", cursor);
                    return;
                }

                try
                {
                    await Task.Delay(pause, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            _logger.LogInformation(
                "Scraped recipe repair done in {Minutes:F1}m: {Recipes} recipes, {Repaired} rows repaired, {Unparsed} rows without a quantity, {Revetted} re-vetted, {Cleared} cleared RequiresRevision",
                (DateTime.UtcNow - started).TotalMinutes, recipes, repaired, unparsed, revetted, cleared);
        }
    }
}
