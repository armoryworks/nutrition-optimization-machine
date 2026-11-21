using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

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

                    var due = await DueRecipeIdsAsync(db, cursor, batchSize, stoppingToken);

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

        /// <summary>
        /// The next <paramref name="take"/> due recipes after <paramref name="cursor"/>, in id order:
        /// those of <see cref="DueRecipeIds"/> and of <see cref="ContainerDueRecipeIds"/>.
        /// </summary>
        public static async Task<List<long>> DueRecipeIdsAsync(ApplicationDbContext db, long cursor, int take, CancellationToken cancellationToken = default)
        {
            var due = await DueRecipeIds(db, cursor).Take(take).ToListAsync(cancellationToken);
            var containers = await ContainerDueRecipeIds(db, cursor).Take(take).ToListAsync(cancellationToken);
            return due.Union(containers).OrderBy(id => id).Take(take).ToList();
        }

        /// <summary>
        /// Recipes after <paramref name="cursor"/>, in id order, whose derived label is missing or
        /// older than a change to their ingredient lines or to a line food's nutrient values (a
        /// backfill adding sodium, say). Hand-authored labels are never due.
        /// </summary>
        public static IQueryable<long> DueRecipeIds(ApplicationDbContext db, long cursor) => db.Recipes
            .AsNoTracking()
            .Where(r => r.Id > cursor && !r.IsDeleted
                && r.RecipeIngredients!.Any(ri => ri.Ingredient.IngredientNutrients.Any())
                && (!r.Nutrition!.Any()
                    || (r.Nutrition!.All(n => n.DateCalculated != null)
                        && (r.Nutrition!.Max(n => n.DateCalculated) < r.RecipeIngredients!.Max(ri => ri.LastModifiedDate ?? ri.CreatedDate)
                            || r.RecipeIngredients!.Any(ri => ri.Ingredient.IngredientNutrients
                                .Any(n => (n.LastModifiedDate ?? n.CreatedDate) > r.Nutrition!.Max(x => x.DateCalculated)))))))
            .OrderBy(r => r.Id)
            .Select(r => r.Id);

        /// <summary>
        /// Recipes after <paramref name="cursor"/>, in id order, with a derived label and a measured
        /// container line on a food with nutrient values, whose label is older than a change to that
        /// food's container sizes, to the container-sizes lane's mode (whole lane or any old-book tier) or
        /// to their book's era, which change the size such a line is weighed at. A size or an era an
        /// undo removed is a change too, so this query reads soft-deleted size and era rows and
        /// filters every other table by hand.
        /// </summary>
        public static IQueryable<long> ContainerDueRecipeIds(ApplicationDbContext db, long cursor) => db.Recipes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.Id > cursor && !r.IsDeleted
                && r.Nutrition!.Any(n => !n.IsDeleted)
                && r.Nutrition!.Where(n => !n.IsDeleted).All(n => n.DateCalculated != null)
                && r.RecipeIngredients!.Any(ri => !ri.IsDeleted && ri.Quantity > 0 && !ri.Measurement.IsDeleted && ri.Measurement.IsContainer
                    && !ri.Ingredient.IsDeleted && ri.Ingredient.IngredientNutrients.Any(n => !n.IsDeleted))
                && (db.IngredientContainerSizes.Any(s => (s.LastModifiedDate ?? s.CreatedDate) > r.Nutrition!.Where(n => !n.IsDeleted).Max(x => x.DateCalculated)
                        && r.RecipeIngredients!.Any(ri => !ri.IsDeleted && ri.IngredientId == s.IngredientId && ri.Quantity > 0 && !ri.Measurement.IsDeleted && ri.Measurement.IsContainer))
                    || db.LaneStates.Any(l => !l.IsDeleted && l.Lane == AiLanes.ContainerSizes
                        && l.Since > r.Nutrition!.Where(n => !n.IsDeleted).Max(x => x.DateCalculated))
                    || db.RecipeSourceEras.Any(e => e.SourceAttribution == r.SourceAttribution
                        && (e.LastModifiedDate ?? e.CreatedDate) > r.Nutrition!.Where(n => !n.IsDeleted).Max(x => x.DateCalculated))))
            .OrderBy(r => r.Id)
            .Select(r => r.Id);

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
