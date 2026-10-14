using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Recipe nutrition = Σ(ingredient per-100 g amount × grams used) / servings.
    ///
    /// Grams per recipe ingredient come from the measurement's category:
    ///   Mass   → quantity × factor-to-grams
    ///   Volume → quantity × factor-to-ml × Ingredient.GramsPerMilliliter (skipped when unknown)
    ///   Count  → quantity × factor × Ingredient.GramsPerItem, else ReferenceServingGrams (skipped when unknown)
    /// Ingredients without nutrition or without a gram conversion contribute nothing (the recipe
    /// response reports them per line, see <see cref="StatusFor"/>); if nothing contributes,
    /// existing rows are left alone. Rows written here carry
    /// DateCalculated so a later hand-authored/seeded set (DateCalculated NULL) is respected.
    /// Values are per serving (Servings ?? RecipeServings ?? 1), matching the seed convention.
    /// </summary>
    public static class NutritionLineStatus
    {
        public const string Counted = "counted";
        public const string NoData = "no-data";
        public const string NoConversion = "no-conversion";
        public const string NotMeasured = "not-measured";
        public const string NotAnIngredient = "not-an-ingredient";
    }

    public class RecipeNutritionService : IRecipeNutritionService
    {
        private const long CategoryMass = 1L;
        private const long CategoryVolume = 2L;
        private const long CategoryCount = 3L;

        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecipeNutritionService> _logger;

        public RecipeNutritionService(ApplicationDbContext db, ILogger<RecipeNutritionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<int> RecalculateForIngredientAsync(long ingredientId)
        {
            var recipeIds = await _db.RecipeIngredients
                .Where(ri => ri.IngredientId == ingredientId)
                .Select(ri => ri.RecipeId)
                .Distinct()
                .ToListAsync();

            var written = 0;
            foreach (var id in recipeIds)
                written += await RecalculateAsync(id);
            return written;
        }

        public async Task<int> RecalculateAsync(long recipeId)
        {
            var recipe = await _db.Recipes
                .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Measurement)
                .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Ingredient)
                    .ThenInclude(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                        .ThenInclude(n => n.DefaultMeasurement)
                .FirstOrDefaultAsync(r => r.Id == recipeId);
            if (recipe == null) return 0;

            var existing = await _db.RecipeNutrition.Where(n => n.RecipeId == recipeId).ToListAsync();
            if (existing.Any(n => n.DateCalculated == null))
            {
                // Seeded / hand-authored label — authoritative, do not derive over it.
                return 0;
            }

            var totals = new Dictionary<long, (decimal amount, string unit)>();
            var contributed = 0;
            foreach (var ri in recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>())
            {
                var ing = ri.Ingredient;
                var hasData = ing?.IngredientNutrients is { Count: > 0 };
                if (StatusFor(ri, hasData) != NutritionLineStatus.Counted) continue;
                var g = GramsFor(ri)!.Value;

                contributed++;
                foreach (var n in ing.IngredientNutrients)
                {
                    var add = n.Amount * g / 100m;
                    var unit = n.Nutrient?.DefaultMeasurement?.Symbol ?? n.Measurement?.Symbol ?? string.Empty;
                    totals[n.NutrientId] = totals.TryGetValue(n.NutrientId, out var cur)
                        ? (cur.amount + add, cur.unit)
                        : (add, unit);
                }
            }

            if (contributed == 0)
            {
                _logger.LogDebug("Recipe {RecipeId}: no ingredient contributed nutrition; leaving label as-is", recipeId);
                return 0;
            }

            var servings = recipe.Servings is > 0 ? recipe.Servings.Value
                : recipe.RecipeServings is > 0 ? recipe.RecipeServings.Value
                : 1m;
            var now = DateTime.UtcNow;

            _db.RecipeNutrition.RemoveRange(existing);
            foreach (var (nutrientId, (amount, unit)) in totals)
            {
                _db.RecipeNutrition.Add(new RecipeNutritionEntity
                {
                    RecipeId = recipeId,
                    NutrientId = nutrientId,
                    Amount = Math.Round(amount / servings, 4),
                    Unit = unit,
                    DateCalculated = now,
                    CreatedDate = now,
                    LastModifiedDate = now
                });
            }
            await _db.SaveChangesAsync();
            _logger.LogInformation("Recipe {RecipeId}: derived {Count} nutrient rows from {Ingredients} ingredient(s)", recipeId, totals.Count, contributed);
            return totals.Count;
        }

        /// <summary>
        /// The recipe's label rows as shown, plus where they come from. On a derived label a nutrient
        /// is <c>Partial</c> when some counted line's food has no value for it, so its total is a
        /// minimum; %DV (<see cref="FdaDailyValues"/>) is given only for a nutrient whose total is
        /// complete, and not at all while a measured ingredient line goes uncounted.
        /// <paramref name="nutrientsByIngredient"/> maps each line's ingredient to the nutrient ids it has.
        /// </summary>
        public static (List<RecipeNutritionSearchModel> Rows, RecipeNutritionSourceModel? Source) BuildLabel(
            RecipeEntity recipe, IReadOnlyDictionary<long, HashSet<long>> nutrientsByIngredient)
        {
            var stored = recipe.Nutrition?.ToList() ?? new List<RecipeNutritionEntity>();
            if (stored.Count == 0) return (new List<RecipeNutritionSearchModel>(), null);

            var derived = stored.Any(n => n.DateCalculated != null);
            var lines = recipe.RecipeIngredients?.ToList() ?? new List<RecipeIngredientEntity>();
            var statuses = lines.Select(ri => (Line: ri, Status: StatusFor(ri, nutrientsByIngredient.ContainsKey(ri.IngredientId)))).ToList();
            var counted = statuses.Where(x => x.Status == NutritionLineStatus.Counted).Select(x => x.Line).ToList();
            var anyUncounted = statuses.Any(x => x.Status is NutritionLineStatus.NoData or NutritionLineStatus.NoConversion);

            var rows = stored.Select(n =>
            {
                var partial = derived && counted.Any(ri =>
                    !nutrientsByIngredient.TryGetValue(ri.IngredientId, out var has) || !has.Contains(n.NutrientId));
                var name = n.Nutrient?.Name ?? string.Empty;
                return new RecipeNutritionSearchModel
                {
                    Id = n.NutrientId,
                    NutrientName = name,
                    Amount = n.Amount,
                    Unit = n.Unit ?? string.Empty,
                    Partial = partial,
                    DailyValuePercent = partial || (derived && anyUncounted)
                        ? null
                        : n.DailyValuePercentage ?? FdaDailyValues.PercentOf(name, n.Amount, n.Unit),
                };
            }).ToList();

            if (!derived)
                return (rows, new RecipeNutritionSourceModel { Kind = "recipe" });
            if (counted.Count == 0)
                return (rows, null);

            var usda = counted.Count(ri => !string.IsNullOrEmpty(ri.Ingredient?.FdcId));
            return (rows, new RecipeNutritionSourceModel
            {
                Kind = usda == 0 ? "catalog" : usda == counted.Count ? "usda" : "mixed",
                UsdaLines = usda,
                CountedLines = counted.Count,
            });
        }

        public static decimal? GramsFor(RecipeIngredientEntity ri) => GramsFor(ri.Quantity, ri.Measurement, ri.Ingredient);

        /// <summary>
        /// Whether a recipe line counts toward the derived label, and if not, why. Lines that are not
        /// ingredients (notes, equipment, references) and unmeasured lines ("salt to taste") are excluded
        /// by design; a measured ingredient line is either counted or missing for a reason the cook can fix.
        /// </summary>
        public static string StatusFor(RecipeIngredientEntity ri, bool ingredientHasNutrients)
        {
            if (ri.LineKind is { } kind && kind != "ingredient") return NutritionLineStatus.NotAnIngredient;
            if (ri.Quantity <= 0) return NutritionLineStatus.NotMeasured;
            if (!ingredientHasNutrients) return NutritionLineStatus.NoData;
            return GramsFor(ri) is > 0 ? NutritionLineStatus.Counted : NutritionLineStatus.NoConversion;
        }

        /// <summary>
        /// <see cref="StatusFor"/> as a query predicate: true for a measured ingredient line the derived
        /// label leaves out (no-data or no-conversion), so a recipe holding one has incomplete totals.
        /// </summary>
        public static readonly Expression<Func<RecipeIngredientEntity, bool>> IsUncountedLine = ri =>
            (ri.LineKind == null || ri.LineKind == "ingredient")
            && ri.Quantity > 0
            && (!ri.Ingredient.IngredientNutrients.Any()
                || ri.Measurement == null
                || (ri.Measurement.BaseUnitConversionFactor ?? 1m) <= 0
                || !(ri.Measurement.MeasurementCategoryId == CategoryMass
                     || (ri.Measurement.MeasurementCategoryId == CategoryVolume && ri.Ingredient.GramsPerMilliliter > 0)
                     || (ri.Measurement.MeasurementCategoryId == CategoryCount && (ri.Ingredient.GramsPerItem > 0 || ri.Ingredient.ReferenceServingGrams > 0))));

        public static decimal? GramsFor(decimal quantity, MeasurementEntity? m, IngredientEntity? ingredient)
        {
            if (m == null) return null;
            var factor = m.BaseUnitConversionFactor ?? 1m;
            switch (m.MeasurementCategoryId)
            {
                case CategoryMass:
                    return quantity * factor;
                case CategoryVolume:
                    var density = ingredient?.GramsPerMilliliter;
                    return density is > 0 ? quantity * factor * density.Value : null;
                case CategoryCount:
                    var per = ingredient?.GramsPerItem is > 0 ? ingredient.GramsPerItem : ingredient?.ReferenceServingGrams;
                    return per is > 0 ? quantity * factor * per.Value : null;
                default:
                    return null;
            }
        }
    }
}
