using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Recipe nutrition = Σ(ingredient per-100 g amount × grams used) / servings.
    ///
    /// Grams per recipe ingredient come from the measurement's category:
    ///   Mass   → quantity × factor-to-grams
    ///   Volume → quantity × factor-to-ml × Ingredient.GramsPerMilliliter (skipped when unknown)
    ///   Count  → quantity × factor × Ingredient.GramsPerItem, else ReferenceServingGrams (skipped when unknown;
    ///            a container unit such as Can, Jar, Bottle or Package is weighed only at an assumed size,
    ///            see <see cref="ContainerWeighing"/>, and such a line is counted-assumed)
    /// Ingredients without nutrition or without a gram conversion contribute nothing (the recipe
    /// response reports them per line, see <see cref="StatusFor"/>); if nothing contributes,
    /// existing rows are left alone, except a derived label whose every measured line is now a can
    /// or package with no stated size, which is removed because it weighed those as items. Rows written here carry
    /// DateCalculated so a later hand-authored/seeded set (DateCalculated NULL) is respected.
    /// Values are per serving (Servings ?? RecipeServings ?? 1), matching the seed convention.
    /// </summary>
    public static class NutritionLineStatus
    {
        public const string Counted = "counted";

        /// <summary>Counted at an assumed container size (<see cref="ContainerWeighing"/>), so the label is an estimate.</summary>
        public const string CountedAssumed = "counted-assumed";
        public const string NoData = "no-data";
        public const string NoConversion = "no-conversion";
        public const string NotMeasured = "not-measured";
        public const string NotAnIngredient = "not-an-ingredient";

        public static bool IsCounted(string status) => status is Counted or CountedAssumed;
    }

    public class RecipeNutritionService : IRecipeNutritionService
    {
        public const long CategoryMass = 1L;
        public const long CategoryVolume = 2L;
        public const long CategoryCount = 3L;

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

            var weighing = await ContainerWeighing.ForRecipeAsync(_db, recipe);
            var totals = new Dictionary<long, (decimal amount, string unit)>();
            var contributed = 0;
            foreach (var ri in recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>())
            {
                var ing = ri.Ingredient;
                var hasData = ing?.IngredientNutrients is { Count: > 0 };
                if (!NutritionLineStatus.IsCounted(StatusFor(ri, hasData, weighing))) continue;
                var g = WeighedGramsFor(ri, weighing)!.Value;

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
                var measured = (recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>())
                    .Where(ri => (ri.LineKind == null || ri.LineKind == "ingredient") && ri.Quantity > 0)
                    .ToList();
                if (existing.Count > 0 && measured.Count > 0 && measured.All(ri => ri.Measurement?.IsContainer == true))
                {
                    _db.RecipeNutrition.RemoveRange(existing);
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Recipe {RecipeId}: every measured line is a can or package with no stated size; derived label removed", recipeId);
                    return 0;
                }
                _logger.LogDebug("Recipe {RecipeId}: no ingredient contributed nutrition; leaving label as-is", recipeId);
                return 0;
            }

            var servings = LabelServings(recipe) ?? 1m;
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
            _logger.LogDebug("Recipe {RecipeId}: derived {Count} nutrient rows from {Ingredients} ingredient(s)", recipeId, totals.Count, contributed);
            return totals.Count;
        }

        /// <summary>
        /// The recipe's label rows as shown, plus where they come from. On a derived label a nutrient
        /// is <c>Partial</c> when some counted line's food has no value for it, so its total is a
        /// minimum; %DV (<see cref="FdaDailyValues"/>) is given only for a nutrient whose total is
        /// complete, and not at all while a measured ingredient line goes uncounted. A line counted at
        /// an assumed container size (<paramref name="weighing"/>) counts, and the source says how many
        /// such lines the label holds, so the page marks the label estimated.
        /// <paramref name="nutrientsByIngredient"/> maps each line's ingredient to the nutrient ids it has.
        /// </summary>
        public static (List<RecipeNutritionSearchModel> Rows, RecipeNutritionSourceModel? Source) BuildLabel(
            RecipeEntity recipe, IReadOnlyDictionary<long, HashSet<long>> nutrientsByIngredient, ContainerWeighing? weighing = null)
        {
            var stored = recipe.Nutrition?.ToList() ?? new List<RecipeNutritionEntity>();
            if (stored.Count == 0) return (new List<RecipeNutritionSearchModel>(), null);

            var derived = stored.Any(n => n.DateCalculated != null);
            var servingsKnown = ServingsKnown(recipe);
            var lines = recipe.RecipeIngredients?.ToList() ?? new List<RecipeIngredientEntity>();
            var statuses = lines.Select(ri => (Line: ri, Status: StatusFor(ri, nutrientsByIngredient.ContainsKey(ri.IngredientId), weighing))).ToList();
            var counted = statuses.Where(x => NutritionLineStatus.IsCounted(x.Status)).Select(x => x.Line).ToList();
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
                    DailyValuePercent = partial || (derived && (anyUncounted || !servingsKnown))
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
                AssumedLines = statuses.Count(x => x.Status == NutritionLineStatus.CountedAssumed),
            });
        }

        /// <summary>How many servings the label divides by, or null when the recipe never said; a derived label then holds whole-recipe totals.</summary>
        public static decimal? LabelServings(RecipeEntity recipe) =>
            recipe.Servings is > 0 ? recipe.Servings.Value
            : recipe.RecipeServings is > 0 ? recipe.RecipeServings.Value
            : null;

        public static bool ServingsKnown(RecipeEntity recipe) => LabelServings(recipe) != null;

        public static decimal? GramsFor(RecipeIngredientEntity ri) => GramsFor(ri.Quantity, ri.Measurement, ri.Ingredient);

        /// <summary>The grams a line weighs: by its unit (<see cref="GramsFor(RecipeIngredientEntity)"/>), else, for a container unit, at its assumed size.</summary>
        public static decimal? WeighedGramsFor(RecipeIngredientEntity ri, ContainerWeighing? weighing) =>
            GramsFor(ri) ?? weighing?.GramsFor(ri);

        /// <summary>
        /// Whether a recipe line counts toward the derived label, and if not, why. Lines that are not
        /// ingredients (notes, equipment, references) and unmeasured lines ("salt to taste") are excluded
        /// by design; a measured ingredient line is either counted or missing for a reason the cook can fix.
        /// </summary>
        public static string StatusFor(RecipeIngredientEntity ri, bool ingredientHasNutrients) => StatusFor(ri, ingredientHasNutrients, null);

        /// <summary>
        /// <see cref="StatusFor(RecipeIngredientEntity, bool)"/>, with a container line that has no
        /// gram weight of its own counted-assumed when <paramref name="weighing"/> weighs it.
        /// </summary>
        public static string StatusFor(RecipeIngredientEntity ri, bool ingredientHasNutrients, ContainerWeighing? weighing)
        {
            if (ri.LineKind is { } kind && kind != "ingredient") return NutritionLineStatus.NotAnIngredient;
            if (ri.Quantity <= 0) return NutritionLineStatus.NotMeasured;
            if (!ingredientHasNutrients) return NutritionLineStatus.NoData;
            if (GramsFor(ri) is > 0) return NutritionLineStatus.Counted;
            return weighing?.GramsFor(ri) is > 0 ? NutritionLineStatus.CountedAssumed : NutritionLineStatus.NoConversion;
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
                     || (ri.Measurement.MeasurementCategoryId == CategoryCount && !ri.Measurement.IsContainer
                         && (ri.Ingredient.GramsPerItem > 0 || ri.Ingredient.ReferenceServingGrams > 0))));

        /// <summary>
        /// <see cref="IsUncountedLine"/>, with a container line read as counted when its food has a
        /// size of the unit's kind among <paramref name="weighable"/> (<see cref="ContainerWeighing.WeighableSizes"/>):
        /// Can, Jar and Bottle by their own kind, Package by any other kind. A query cannot read the
        /// line's text, so a size word, a drained line or the era are not looked at here.
        /// </summary>
        public static Expression<Func<RecipeIngredientEntity, bool>> IsUncountedLineWith(IQueryable<IngredientContainerSizeEntity> weighable) => ri =>
            (ri.LineKind == null || ri.LineKind == "ingredient")
            && ri.Quantity > 0
            && (!ri.Ingredient.IngredientNutrients.Any()
                || ri.Measurement == null
                || (ri.Measurement.BaseUnitConversionFactor ?? 1m) <= 0
                || !(ri.Measurement.MeasurementCategoryId == CategoryMass
                     || (ri.Measurement.MeasurementCategoryId == CategoryVolume && ri.Ingredient.GramsPerMilliliter > 0)
                     || (ri.Measurement.MeasurementCategoryId == CategoryCount && !ri.Measurement.IsContainer
                         && (ri.Ingredient.GramsPerItem > 0 || ri.Ingredient.ReferenceServingGrams > 0))
                     || (ri.Measurement.MeasurementCategoryId == CategoryCount && ri.Measurement.IsContainer
                         && weighable.Any(s => s.IngredientId == ri.IngredientId
                             && (s.ContainerKind == ri.Measurement.Name.ToLower()
                                 || (ri.Measurement.Name == IngredientLineParser.Package
                                     && s.ContainerKind != ContainerKinds.Can && s.ContainerKind != ContainerKinds.Jar && s.ContainerKind != ContainerKinds.Bottle))))));

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
                    if (m.IsContainer) return null;
                    var per = ingredient?.GramsPerItem is > 0 ? ingredient.GramsPerItem : ingredient?.ReferenceServingGrams;
                    return per is > 0 ? quantity * factor * per.Value : null;
                default:
                    return null;
            }
        }
    }
}
