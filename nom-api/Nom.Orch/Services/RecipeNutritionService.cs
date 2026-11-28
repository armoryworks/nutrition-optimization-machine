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
    /// existing rows are left alone, except a derived label of a recipe with a measured line in a
    /// container unit (can, jar, bottle, package), which is removed because it weighed that line as
    /// items or at an assumed size that no longer counts (the lane paused, the size retired). Rows written here carry
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
        private const int RecalculateChunk = 500;

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

        private IQueryable<RecipeEntity> Labelled() => _db.Recipes
            .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Measurement)
            .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Ingredient)
                .ThenInclude(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                    .ThenInclude(n => n.DefaultMeasurement);

        public async Task<int> RecalculateAsync(long recipeId)
        {
            var recipe = await Labelled().FirstOrDefaultAsync(r => r.Id == recipeId);
            if (recipe == null) return 0;

            var existing = await _db.RecipeNutrition.Where(n => n.RecipeId == recipeId).ToListAsync();
            var check = await _db.RecipeNutritionChecks.FirstOrDefaultAsync(c => c.RecipeId == recipeId);
            var (written, changed) = await DeriveAsync(recipe, existing, check);
            if (changed) await _db.SaveChangesAsync();
            return written;
        }

        /// <summary>
        /// One line's inputs to the label (<see cref="InputPrint(IEnumerable{InputLine}, long?, decimal?)"/>): the line's food,
        /// amount, unit, kind and alcohol retention; the unit's category, factor and container mark; the food's weights
        /// (grams per millilitre, per item, per reference serving); and, for a line in a container unit, the line's text and
        /// the food's description, which the assumed size is read from.
        /// </summary>
        public readonly record struct InputLine(long Food, decimal Quantity, long Measurement, string? Kind, decimal? Retention, string? Basis,
            long? Category, decimal? Factor, bool Container, decimal? GramsPerMilliliter, decimal? GramsPerItem, decimal? ReferenceServingGrams, string? Text, string? Description);

        /// <summary>
        /// The fingerprint of what a recipe's label is derived from on its own lines (staging checkpoint 6, R9): every live
        /// line's <see cref="InputLine"/>, in any order, and the servings. A write that changes none of these (an order-only
        /// pass, a text or position edit of a line not in a container unit) leaves the label as it is; a writer that gives a
        /// food a weight and touches its lines (the FDC portion backfill, a relink that inherits measures) changes it
        /// (review CK11-DM-2).
        /// </summary>
        public static long InputPrint(IEnumerable<InputLine> lines, long? servings, decimal? recipeServings)
        {
            var hash = new DietFoodCatalog.Fnv();
            foreach (var line in lines.Select(LinePrint).OrderBy(p => p))
                hash.Add(line);
            hash.Add("|").Add(servings).Add(recipeServings);
            return hash.Value;
        }

        private static long LinePrint(InputLine l)
        {
            var hash = new DietFoodCatalog.Fnv();
            hash.Add(l.Food).Add(l.Quantity).Add(l.Measurement).Add(l.Kind).Add(l.Retention).Add(l.Basis)
                .Add(l.Category).Add(l.Factor).Add(l.Container ? 1L : 0L).Add(l.GramsPerMilliliter).Add(l.GramsPerItem).Add(l.ReferenceServingGrams);
            if (l.Container) hash.Add(l.Text).Add(l.Description);
            return hash.Value;
        }

        /// <summary>The <see cref="InputLine"/> of a line read with its measurement and food.</summary>
        public static InputLine InputOf(RecipeIngredientEntity ri) =>
            new(ri.IngredientId, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis,
                ri.Measurement?.MeasurementCategoryId, ri.Measurement?.BaseUnitConversionFactor, ri.Measurement?.IsContainer == true,
                ri.Ingredient?.GramsPerMilliliter, ri.Ingredient?.GramsPerItem, ri.Ingredient?.ReferenceServingGrams, ri.RawLine, ri.Ingredient?.Description);

        private static long InputPrint(RecipeEntity recipe) =>
            InputPrint((recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>()).Where(ri => !ri.IsDeleted).Select(InputOf), recipe.Servings, recipe.RecipeServings);

        private void Checked(RecipeEntity recipe, RecipeNutritionCheckEntity? check, DateTime now)
        {
            if (check == null) _db.RecipeNutritionChecks.Add(new RecipeNutritionCheckEntity { RecipeId = recipe.Id, InputPrint = InputPrint(recipe), CheckedAt = now });
            else
            {
                check.InputPrint = InputPrint(recipe);
                check.CheckedAt = now;
            }
        }

        public async Task<int> RecalculateManyAsync(IReadOnlyCollection<long> recipeIds)
        {
            var written = 0;
            foreach (var part in recipeIds.Distinct().Order().Chunk(RecalculateChunk))
            {
                var recipes = await Labelled().AsSplitQuery().Where(r => part.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                var labels = (await _db.RecipeNutrition.Where(n => part.Contains(n.RecipeId)).ToListAsync()).ToLookup(n => n.RecipeId);
                var checks = await _db.RecipeNutritionChecks.Where(c => part.Contains(c.RecipeId)).ToDictionaryAsync(c => c.RecipeId);
                var changed = false;
                foreach (var id in part)
                {
                    if (!recipes.TryGetValue(id, out var recipe)) continue;
                    var (rows, wrote) = await DeriveAsync(recipe, labels[id].ToList(), checks.GetValueOrDefault(id));
                    written += rows;
                    changed |= wrote;
                }
                if (changed) await _db.SaveChangesAsync();
            }
            return written;
        }

        private async Task<(int Written, bool Changed)> DeriveAsync(RecipeEntity recipe, List<RecipeNutritionEntity> existing, RecipeNutritionCheckEntity? check)
        {
            var recipeId = recipe.Id;
            if (existing.Any(n => n.DateCalculated == null))
            {
                // Seeded / hand-authored label — authoritative, do not derive over it.
                return (0, false);
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
                var lostAlcohol = AlcoholLost(ri, ing.IngredientNutrients, g);
                foreach (var n in ing.IngredientNutrients)
                {
                    var add = Retained(n.Nutrient?.Name, n.Amount * g / 100m, lostAlcohol);
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
                Checked(recipe, check, DateTime.UtcNow);
                if (existing.Count > 0 && measured.Any(ri => ri.Measurement?.IsContainer == true))
                {
                    _db.RecipeNutrition.RemoveRange(existing);
                    _logger.LogInformation("Recipe {RecipeId}: nothing counts and a measured line is in a container unit, which the label may have weighed at an assumed size; derived label removed", recipeId);
                    return (0, true);
                }
                _logger.LogDebug("Recipe {RecipeId}: no ingredient contributed nutrition; leaving label as-is", recipeId);
                return (0, true);
            }

            var servings = LabelServings(recipe) ?? 1m;
            var now = DateTime.UtcNow;
            Checked(recipe, check, now);

            if (existing.Count == totals.Count && existing.Select(e => e.NutrientId).Distinct().Count() == existing.Count && existing.All(e => totals.TryGetValue(e.NutrientId, out var t)
                    && Math.Round(t.amount / servings, 4) == e.Amount && string.Equals(t.unit ?? string.Empty, e.Unit ?? string.Empty, StringComparison.Ordinal)))
            {
                _logger.LogDebug("Recipe {RecipeId}: the label derives as it is stored; left as it is", recipeId);
                return (0, true);
            }

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
            _logger.LogDebug("Recipe {RecipeId}: derived {Count} nutrient rows from {Ingredients} ingredient(s)", recipeId, totals.Count, contributed);
            return (totals.Count, true);
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

        public const string CaloriesNutrientName = "Calories";

        /// <summary>
        /// The recipe page's alcohol note from the recipe's lines (with their foods and measurements) and each line
        /// food's alcohol per 100 g: the drinks' grams per serving as written and after cooking, the basis of the
        /// drink line with the most alcohol, the flavourings and the drink lines that cannot be counted; null when
        /// no line carries alcohol. A line is named by its food, or by its text; for a recipe of a facts-only source by
        /// NOM's own wording of it (<see cref="Support.ProseWithholding.LineName(RecipeEntity, RecipeIngredientEntity)"/>), never the publisher's line;
        /// a line's text is served as a reader sees it, its HTML entities read (<see cref="Support.ProseWithholding.Shown"/>; staging checkpoint 9, U15).
        /// </summary>
        public static RecipeAlcoholNoteModel? AlcoholNote(RecipeEntity recipe, IReadOnlyDictionary<long, decimal> alcoholPer100g)
        {
            var lines = (recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>()).Where(ri => !ri.IsDeleted).OrderBy(ri => ri.SortOrder).ToList();
            var count = Support.RecipeAlcohol.Count(lines.Select(ri => new Support.AlcoholLineFacts(
                ri.IngredientId, ri.RawLine, ri.Ingredient?.Name, ri.LineKind, ri.Quantity, GramsFor(ri),
                alcoholPer100g.TryGetValue(ri.IngredientId, out var per100) ? per100 : null,
                Support.RecipeAlcohol.IsFlavouringFood(ri.Ingredient?.Name, ri.Ingredient?.FdcCategoryId),
                ri.AlcoholRetention, ri.AlcoholRetentionBasis,
                recipe.SourceRights == Support.SourceRightsRule.FactsOnly ? Support.ProseWithholding.LineName(recipe, ri) : null)), LabelServings(recipe));
            if (!count.Any) return null;

            var counted = count.Drinks.Where(d => d.AsWritten != null).ToList();
            var main = counted.OrderByDescending(d => d.AsWritten).FirstOrDefault();
            var basis = Support.AlcoholRetention.ReadBasis(main?.RetentionBasis);
            var row = basis == null ? null : Support.AlcoholRetention.RowFor(basis.Value.Kind, basis.Value.Method, basis.Value.Minutes);
            static string Shown(Support.AlcoholDrinkLine d) => !string.IsNullOrWhiteSpace(d.FoodName) ? d.FoodName!.Trim() : (d.Label ?? Support.ProseWithholding.Shown(d.RawLine) ?? string.Empty).Trim();
            return new RecipeAlcoholNoteModel
            {
                Drinks = counted.Select(Shown).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Flavourings = count.Flavourings.ToList(),
                Uncounted = count.Uncounted.Select(d => d.Label ?? (!string.IsNullOrWhiteSpace(d.RawLine) ? Support.ProseWithholding.Shown(d.RawLine)!.Trim() : Shown(d))).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                AsWrittenGrams = Math.Round(count.AsWrittenPerServing, 2),
                RetainedGrams = Math.Round(count.RetainedPerServing, 2),
                FlavouringGrams = Math.Round(count.FlavouringPerServing, 2),
                PerServing = count.ServingsKnown,
                Basis = basis?.Kind,
                Method = basis?.Method,
                Minutes = basis?.Minutes,
                RetainedPercent = count.DrinkAsWritten > 0m ? (int)Math.Round(count.DrinkRetained / count.DrinkAsWritten * 100m) : null,
                UsdaCode = row?.Code,
                UpTo = count.UpperBound,
                Reason = basis?.Reason,
                NotRead = counted.Any(d => d.Retention == null),
            };
        }

        /// <summary>
        /// Grams of the line's alcohol that do not stay after cooking: the food's alcohol per 100 g times the line's
        /// grams times (1 − <see cref="RecipeIngredientEntity.AlcoholRetention"/>); 0 when the retention has not been read.
        /// </summary>
        public static decimal AlcoholLost(RecipeIngredientEntity ri, IEnumerable<Nom.Data.Nutrient.IngredientNutrientEntity> nutrients, decimal grams)
        {
            if (ri.AlcoholRetention is not { } factor || factor >= 1m || factor < 0m) return 0m;
            var alcohol = nutrients.FirstOrDefault(n => Support.DietCriteriaMatcher.IsAlcohol(n.Nutrient?.Name));
            return alcohol == null || alcohol.Amount <= 0m ? 0m : alcohol.Amount * grams / 100m * (1m - factor);
        }

        /// <summary>
        /// A line's amount of one nutrient after cooking loss (alcohol design A7): alcohol less what does not stay,
        /// energy less 6.93 kcal for each gram of it (<see cref="Support.AlcoholRetention.KcalPerGram"/>, never below 0),
        /// every other nutrient as stored. The food's stored values are never changed.
        /// </summary>
        public static decimal Retained(string? nutrientName, decimal amount, decimal lostAlcohol)
        {
            if (lostAlcohol <= 0m) return amount;
            if (Support.DietCriteriaMatcher.IsAlcohol(nutrientName)) return Math.Max(0m, amount - lostAlcohol);
            if (string.Equals(nutrientName?.Trim(), CaloriesNutrientName, StringComparison.OrdinalIgnoreCase)) return Math.Max(0m, amount - lostAlcohol * Support.AlcoholRetention.KcalPerGram);
            return amount;
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

        public static decimal? GramsFor(decimal quantity, MeasurementEntity? m, IngredientEntity? ingredient) =>
            m == null ? null : GramsFor(quantity, m.MeasurementCategoryId, m.BaseUnitConversionFactor, m.IsContainer, ingredient?.GramsPerMilliliter, ingredient?.GramsPerItem, ingredient?.ReferenceServingGrams);

        /// <summary><see cref="GramsFor(decimal, MeasurementEntity?, IngredientEntity?)"/> on the measurement's and food's fields, for a query that reads only those.</summary>
        public static decimal? GramsFor(decimal quantity, long categoryId, decimal? conversionFactor, bool isContainer, decimal? gramsPerMilliliter, decimal? gramsPerItem, decimal? referenceServingGrams)
        {
            var factor = conversionFactor ?? 1m;
            switch (categoryId)
            {
                case CategoryMass:
                    return quantity * factor;
                case CategoryVolume:
                    return gramsPerMilliliter is > 0 ? quantity * factor * gramsPerMilliliter.Value : null;
                case CategoryCount:
                    if (isContainer) return null;
                    var per = gramsPerItem is > 0 ? gramsPerItem : referenceServingGrams;
                    return per is > 0 ? quantity * factor * per.Value : null;
                default:
                    return null;
            }
        }
    }
}
