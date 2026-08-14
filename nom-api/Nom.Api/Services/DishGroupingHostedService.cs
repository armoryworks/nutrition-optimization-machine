using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;

namespace Nom.Api.Services
{
    /// <summary>
    /// Continuously classifies unclassified recipes into canonical dish groups.
    /// Suggestions come from the registered <see cref="IDishGroupSuggester"/> —
    /// heuristic by default, AI-backed when Ai:OllamaUrl is configured — and
    /// every assignment stays admin-correctable via the DishGroup API.
    ///
    /// On by default (DishGrouping:Enabled=false to disable);
    /// DishGrouping:IntervalMinutes (default 30) between sweeps,
    /// DishGrouping:BatchSize (default 50) recipes per sweep.
    /// </summary>
    public class DishGroupingHostedService : BackgroundService
    {
        private const int SuggesterBatch = 10;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DishGroupingHostedService> _logger;

        public DishGroupingHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<DishGroupingHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("DishGrouping:Enabled", true))
            {
                _logger.LogInformation("Dish grouping is disabled (DishGrouping:Enabled=false)");
                return;
            }

            var interval = TimeSpan.FromMinutes(
                Math.Max(1, _configuration.GetValue("DishGrouping:IntervalMinutes", 30)));
            var batchSize = Math.Clamp(_configuration.GetValue("DishGrouping:BatchSize", 50), 1, 500);

            _logger.LogInformation("Dish grouping enabled: every {Minutes}m, up to {Batch} recipes per sweep",
                interval.TotalMinutes, batchSize);

            await SafeDelay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(batchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dish grouping sweep failed");
                }

                await SafeDelay(interval, stoppingToken);
            }
        }

        private async Task SweepAsync(int batchSize, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var suggester = scope.ServiceProvider.GetRequiredService<IDishGroupSuggester>();
            var groups = scope.ServiceProvider.GetRequiredService<IDishGroupService>();

            var unclassified = await db.Recipes
                .AsNoTracking()
                .Where(r => r.DishGroupId == null)
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .Select(r => new DishGroupCandidate(
                    r.Id,
                    r.Name,
                    r.RecipeIngredients!
                        .Where(ri => ri.Ingredient != null)
                        .Select(ri => ri.Ingredient!.Name)
                        .Take(8)
                        .ToList()))
                .ToListAsync(cancellationToken);

            if (unclassified.Count == 0)
            {
                return;
            }

            var assigned = 0;
            foreach (var chunk in unclassified.Chunk(SuggesterBatch))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = chunk.ToList();
                var suggestions = await suggester.SuggestAsync(batch, cancellationToken);
                for (var i = 0; i < batch.Count && i < suggestions.Count; i++)
                {
                    var name = suggestions[i];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var group = await groups.GetOrCreateAsync(name);
                    if (await groups.AssignAsync(batch[i].RecipeId, group.Id))
                    {
                        assigned++;
                    }
                }
            }

            _logger.LogInformation("Dish grouping sweep: {Assigned}/{Seen} recipes classified",
                assigned, unclassified.Count);
        }

        private static async Task SafeDelay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }
}
