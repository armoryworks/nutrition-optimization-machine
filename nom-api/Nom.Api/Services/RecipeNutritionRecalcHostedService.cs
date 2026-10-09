using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Keeps derived recipe nutrition current as ingredients gain USDA data: sweeps recipes that
    /// use an ingredient with nutrient facts and either have no derived label yet or had an
    /// ingredient link change after their label was calculated. Hand-authored labels are never
    /// touched (RecipeNutritionService keeps them). Sweeps by recipe id, so a recipe whose amounts
    /// can't be converted to grams is passed over rather than retried forever.
    ///
    /// NutritionRecalc:Enabled=false disables it; NutritionRecalc:BatchSize (default 200);
    /// NutritionRecalc:IdleMinutes (default 15) between full sweeps.
    /// </summary>
    public class RecipeNutritionRecalcHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecipeNutritionRecalcHostedService> _logger;

        public RecipeNutritionRecalcHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<RecipeNutritionRecalcHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("NutritionRecalc:Enabled", true))
            {
                _logger.LogInformation("Recipe nutrition recalculation is disabled (NutritionRecalc:Enabled=false)");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("NutritionRecalc:BatchSize", 200), 1, 2000);
            var idle = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("NutritionRecalc:IdleMinutes", 15)));
            if (!await Delay(TimeSpan.FromMinutes(1), stoppingToken)) return;

            long cursor = 0;
            int updated = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(200);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var nutrition = scope.ServiceProvider.GetRequiredService<IRecipeNutritionService>();

                    var due = await db.Recipes
                        .AsNoTracking()
                        .Where(r => r.Id > cursor && !r.IsDeleted
                            && r.RecipeIngredients!.Any(ri => ri.Ingredient.IngredientNutrients.Any())
                            && (!r.Nutrition!.Any()
                                || (r.Nutrition!.All(n => n.DateCalculated != null)
                                    && r.Nutrition!.Max(n => n.DateCalculated) < r.RecipeIngredients!.Max(ri => ri.LastModifiedDate ?? ri.CreatedDate))))
                        .OrderBy(r => r.Id)
                        .Select(r => r.Id)
                        .Take(batchSize)
                        .ToListAsync(stoppingToken);

                    if (due.Count == 0)
                    {
                        if (updated > 0) _logger.LogInformation("Recipe nutrition sweep done: {Count} recipes recalculated", updated);
                        cursor = 0;
                        updated = 0;
                        delay = idle;
                    }
                    else
                    {
                        foreach (var id in due)
                        {
                            stoppingToken.ThrowIfCancellationRequested();
                            if (await nutrition.RecalculateAsync(id) > 0) updated++;
                        }
                        cursor = due[^1];
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recipe nutrition sweep after recipe {Cursor} failed", cursor);
                    delay = TimeSpan.FromMinutes(5);
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
