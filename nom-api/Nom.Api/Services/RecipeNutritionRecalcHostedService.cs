using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// Keeps derived recipe nutrition current as ingredients gain USDA data: sweeps recipes that
    /// use an ingredient with nutrient facts and either have no derived label yet or had an
    /// ingredient link change after their label was last checked. Hand-authored labels are never
    /// touched (RecipeNutritionService keeps them). Sweeps by recipe id, so a recipe whose amounts
    /// can't be converted to grams is passed over rather than retried forever. Only recipes whose
    /// inputs changed are derived again (staging checkpoint 6, R9): a recipe due only because its
    /// lines were written is first compared by the fingerprint of its lines' inputs
    /// (<see cref="RecipeNutritionService.InputPrint(IEnumerable{RecipeNutritionService.InputLine}, long?, decimal?)"/>),
    /// and when that is unchanged only its check (<c>recipe."RecipeNutritionCheck"</c>) is moved on; a label that
    /// derives as it is stored is not rewritten.
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

        /// <summary>What one batch of the sweep did: the last recipe id it covered (null when nothing was due), the recipes derived again and those only checked.</summary>
        public sealed record SweepBatch(long? LastId, int Derived, int Written, int Checked);

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
            int updated = 0, checkedOnly = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(200);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var nutrition = scope.ServiceProvider.GetRequiredService<IRecipeNutritionService>();

                    var batch = await SweepBatchAsync(db, nutrition, cursor, batchSize, stoppingToken);

                    if (batch.LastId == null)
                    {
                        if (updated > 0 || checkedOnly > 0) _logger.LogInformation("Recipe nutrition sweep done: {Count} recipes recalculated, {Checked} unchanged by their inputs", updated, checkedOnly);
                        cursor = 0;
                        updated = 0;
                        checkedOnly = 0;
                        delay = idle;
                    }
                    else
                    {
                        updated += batch.Written;
                        checkedOnly += batch.Checked;
                        cursor = batch.LastId.Value;
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
        /// One batch of the sweep after <paramref name="cursor"/>: of the due recipes (<see cref="DueRecipeIdsAsync"/>), those
        /// due only because their lines were written and whose lines' inputs have the fingerprint of their last check are
        /// only checked again; the others are derived (<see cref="IRecipeNutritionService.RecalculateAsync"/>), which writes
        /// the label only when it comes out different.
        /// </summary>
        public static async Task<SweepBatch> SweepBatchAsync(ApplicationDbContext db, IRecipeNutritionService nutrition, long cursor, int take, CancellationToken cancellationToken = default)
        {
            var due = await DueRecipeIdsAsync(db, cursor, take, cancellationToken);
            if (due.Count == 0) return new SweepBatch(null, 0, 0, 0);
            var full = (await FoodDueRecipeIds(db, due).ToListAsync(cancellationToken)).ToHashSet();
            full.UnionWith(await ContainerDueRecipeIds(db, cursor).Where(id => due.Contains(id)).ToListAsync(cancellationToken));
            var lineOnly = due.Where(id => !full.Contains(id)).ToList();
            var checkedOnly = 0;
            if (lineOnly.Count > 0)
            {
                var prints = await InputPrintsAsync(db, lineOnly, cancellationToken);
                var checks = await db.RecipeNutritionChecks.Where(c => lineOnly.Contains(c.RecipeId)).ToDictionaryAsync(c => c.RecipeId, cancellationToken);
                var now = DateTime.UtcNow;
                foreach (var id in lineOnly)
                {
                    if (checks.TryGetValue(id, out var check) && prints.TryGetValue(id, out var print) && check.InputPrint == print)
                    {
                        check.CheckedAt = now;
                        checkedOnly++;
                    }
                    else full.Add(id);
                }
                if (checkedOnly > 0) await db.SaveChangesAsync(cancellationToken);
            }
            var written = 0;
            foreach (var id in due.Where(full.Contains))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await nutrition.RecalculateAsync(id) > 0) written++;
            }
            return new SweepBatch(due[^1], full.Count, written, checkedOnly);
        }

        private static async Task<Dictionary<long, long>> InputPrintsAsync(ApplicationDbContext db, List<long> recipeIds, CancellationToken cancellationToken)
        {
            var servings = await db.Recipes.AsNoTracking().Where(r => recipeIds.Contains(r.Id)).Select(r => new { r.Id, r.Servings, r.RecipeServings }).ToListAsync(cancellationToken);
            var lines = (await db.RecipeIngredients.AsNoTracking()
                    .Where(ri => recipeIds.Contains(ri.RecipeId))
                    .Select(ri => new
                    {
                        ri.RecipeId,
                        Line = new RecipeNutritionService.InputLine(ri.IngredientId, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis,
                            (long?)ri.Measurement.MeasurementCategoryId, ri.Measurement.BaseUnitConversionFactor, (bool?)ri.Measurement.IsContainer ?? false,
                            ri.Ingredient.GramsPerMilliliter, ri.Ingredient.GramsPerItem, ri.Ingredient.ReferenceServingGrams, ri.RawLine, ri.Ingredient.Description),
                    })
                    .ToListAsync(cancellationToken))
                .ToLookup(l => l.RecipeId, l => l.Line);
            return servings.ToDictionary(r => r.Id, r => RecipeNutritionService.InputPrint(lines[r.Id], r.Servings, r.RecipeServings));
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
        /// Recipes after <paramref name="cursor"/>, in id order, whose derived label is missing (and was never checked) or
        /// whose last check (the later of the label's calculation and its <c>RecipeNutritionCheck</c>) is older than a change
        /// to their ingredient lines or to a line food's nutrient values (a backfill adding sodium, say), or, while they have
        /// no label, older than a change to a line food (a weight that lets a line be weighed, review CK11-DM-3).
        /// Hand-authored labels are never due.
        /// </summary>
        public static IQueryable<long> DueRecipeIds(ApplicationDbContext db, long cursor) =>
            from r in db.Recipes.AsNoTracking()
            where r.Id > cursor && !r.IsDeleted
                  && r.RecipeIngredients!.Any(ri => ri.Ingredient.IngredientNutrients.Any())
                  && r.Nutrition!.All(n => n.DateCalculated != null)
            let label = r.Nutrition!.Max(n => n.DateCalculated)
            let check = db.RecipeNutritionChecks.Where(c => c.RecipeId == r.Id).Select(c => (DateTime?)c.CheckedAt).FirstOrDefault()
            let since = check != null && (label == null || check > label) ? check : label
            where since == null
                  || r.RecipeIngredients!.Any(ri => (ri.LastModifiedDate ?? ri.CreatedDate) > since)
                  || r.RecipeIngredients!.Any(ri => ri.Ingredient.IngredientNutrients.Any(n => (n.LastModifiedDate ?? n.CreatedDate) > since))
                  || (!r.Nutrition!.Any() && r.RecipeIngredients!.Any(ri => (ri.Ingredient.LastModifiedDate ?? ri.Ingredient.CreatedDate) > since))
            orderby r.Id
            select r.Id;

        /// <summary>Of the recipes, those due for a reason beyond their own lines: no label and no check yet, a line food's nutrient values changed since the last check, or, while they have no label, a line food changed since.</summary>
        public static IQueryable<long> FoodDueRecipeIds(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds) =>
            from r in db.Recipes.AsNoTracking()
            where recipeIds.Contains(r.Id)
            let label = r.Nutrition!.Max(n => n.DateCalculated)
            let check = db.RecipeNutritionChecks.Where(c => c.RecipeId == r.Id).Select(c => (DateTime?)c.CheckedAt).FirstOrDefault()
            let since = check != null && (label == null || check > label) ? check : label
            where since == null
                  || r.RecipeIngredients!.Any(ri => ri.Ingredient.IngredientNutrients.Any(n => (n.LastModifiedDate ?? n.CreatedDate) > since))
                  || (!r.Nutrition!.Any() && r.RecipeIngredients!.Any(ri => (ri.Ingredient.LastModifiedDate ?? ri.Ingredient.CreatedDate) > since))
            select r.Id;

        /// <summary>
        /// Recipes after <paramref name="cursor"/>, in id order, with a derived label or a check and no
        /// hand-authored label (a recipe that could not be weighed yet is due too, review CK11-DM-3), and a measured
        /// container line on a food with nutrient values, whose last check is older than a change to that
        /// food's container sizes, to the container-sizes lane's mode (whole lane or any old-book tier) or
        /// to their book's era, which change the size such a line is weighed at. A size or an era an
        /// undo removed is a change too, so this query reads soft-deleted size and era rows and
        /// filters every other table by hand.
        /// </summary>
        public static IQueryable<long> ContainerDueRecipeIds(ApplicationDbContext db, long cursor) =>
            from r in db.Recipes.IgnoreQueryFilters().AsNoTracking()
            where r.Id > cursor && !r.IsDeleted
                  && r.Nutrition!.Where(n => !n.IsDeleted).All(n => n.DateCalculated != null)
                  && r.RecipeIngredients!.Any(ri => !ri.IsDeleted && ri.Quantity > 0 && !ri.Measurement.IsDeleted && ri.Measurement.IsContainer
                      && !ri.Ingredient.IsDeleted && ri.Ingredient.IngredientNutrients.Any(n => !n.IsDeleted))
            let label = r.Nutrition!.Where(n => !n.IsDeleted).Max(x => x.DateCalculated)
            let check = db.RecipeNutritionChecks.Where(c => c.RecipeId == r.Id).Select(c => (DateTime?)c.CheckedAt).FirstOrDefault()
            let since = check != null && (label == null || check > label) ? check : label
            where since != null
            where db.IngredientContainerSizes.Any(s => (s.LastModifiedDate ?? s.CreatedDate) > since
                      && r.RecipeIngredients!.Any(ri => !ri.IsDeleted && ri.IngredientId == s.IngredientId && ri.Quantity > 0 && !ri.Measurement.IsDeleted && ri.Measurement.IsContainer))
                  || db.LaneStates.Any(l => !l.IsDeleted && l.Lane == AiLanes.ContainerSizes && l.Since > since)
                  || db.RecipeSourceEras.Any(e => e.SourceAttribution == r.SourceAttribution && (e.LastModifiedDate ?? e.CreatedDate) > since)
            orderby r.Id
            select r.Id;

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
