using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Works through untagged recipes, asking the local model which kitchen tools each needs and
    /// queuing uncommon equipment it notices for admin review. Recipes stay covered by keyword
    /// detection until tagged; author-listed tools always win over AI tags.
    ///
    /// Runs only when an Ollama URL is configured (Ai:BatchOllamaUrl or Ai:OllamaUrl).
    /// ToolTagging:Enabled=false disables it; ToolTagging:BatchSize (default 6) recipes per model
    /// call; ToolTagging:PauseSeconds (default 5) between calls so interactive AI isn't starved;
    /// ToolTagging:IdleMinutes (default 30) between checks once everything is tagged.
    /// </summary>
    public class RecipeToolTaggingHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecipeToolTaggingHostedService> _logger;

        public RecipeToolTaggingHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<RecipeToolTaggingHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("ToolTagging:Enabled", true))
            {
                _logger.LogInformation("Recipe tool tagging is disabled (ToolTagging:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("ToolTagging:BatchSize", 6), 1, 20);
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("ToolTagging:PauseSeconds", 5)));
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("ToolTagging:IdleMinutes", 30)));
            var backoff = TimeSpan.FromMinutes(5);

            using (var scope = _scopeFactory.CreateScope())
            {
                var tagger = scope.ServiceProvider.GetRequiredService<IRecipeToolTagger>();
                if (!tagger.IsConfigured)
                {
                    _logger.LogInformation("Recipe tool tagging idle: no Ollama URL configured");
                    return;
                }
                _logger.LogInformation("Recipe tool tagging enabled: model {Model}, {Batch} recipes per call", tagger.ModelName, batchSize);
            }

            await SafeDelay(TimeSpan.FromMinutes(1), stoppingToken);

            var taggedSinceLog = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = pause;
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var lane = scope.ServiceProvider.GetRequiredService<IKitchenToolTaggingService>();
                    var result = await lane.TagNextBatchAsync(batchSize, stoppingToken);

                    if (result == null)
                    {
                        delay = backoff;
                    }
                    else if (result.Seen == 0)
                    {
                        if (taggedSinceLog > 0)
                        {
                            _logger.LogInformation("Recipe tool tagging caught up ({Count} recipes this run)", taggedSinceLog);
                            taggedSinceLog = 0;
                        }
                        delay = idle;
                    }
                    else
                    {
                        taggedSinceLog += result.Tagged;
                        if (taggedSinceLog >= 500)
                        {
                            _logger.LogInformation("Recipe tool tagging: {Count} more recipes tagged", taggedSinceLog);
                            taggedSinceLog = 0;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recipe tool tagging batch failed");
                    delay = backoff;
                }

                await SafeDelay(delay, stoppingToken);
            }
        }

        private static async Task SafeDelay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
