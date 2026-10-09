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
    /// Curation:AutoApprove:IdleMinutes (default 30) between sweeps.
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

            if (!await Delay(TimeSpan.FromMinutes(2), stoppingToken)) return;
            _logger.LogInformation("Recipe auto-approval enabled ({Batch} recipes per batch, sweeping every {Idle}m)", batchSize, idle.TotalMinutes);

            long cursor = 0;
            int approved = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var curation = scope.ServiceProvider.GetRequiredService<IRecipeAutoCurationService>();
                    var batch = await curation.AutoApproveBatchAsync(cursor, batchSize, stoppingToken);
                    if (batch.Examined == 0)
                    {
                        if (approved > 0) _logger.LogInformation("Recipe auto-approval sweep done: {Approved} approved", approved);
                        approved = 0;
                        cursor = 0;
                        delay = idle;
                    }
                    else
                    {
                        approved += batch.Approved;
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
