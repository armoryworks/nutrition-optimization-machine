using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The assumed weight of a recipe's unsized container lines ("1 can black beans"), for the
    /// label and the recipe page. A line in a container unit is weighed by
    /// <see cref="ContainerSizeResolver"/> at the size of its food's active row in
    /// nutrient.IngredientContainerSize (or the cited can table), by the recipe's era, and only
    /// while the container-sizes lane is live on the lane frame; otherwise it stays uncounted. A
    /// line of an old or undated book that would take another era's size (<see cref="ContainerBasis.OtherEra"/>)
    /// is weighed only while the lane's old-book tier is live too. A line without a container word
    /// in its text is read by its unit ("1 Jar" of the food).
    /// Every weight it gives is an estimate and is shown as one.
    /// </summary>
    public sealed class ContainerWeighing
    {
        private static readonly IReadOnlyList<ContainerSizeCandidate> NoSizes = Array.Empty<ContainerSizeCandidate>();

        private readonly IReadOnlyDictionary<long, IReadOnlyList<ContainerSizeCandidate>> _sizes;

        public const string OtherEraNotLive = "an old or undated book's size from another era, and the lane's old-book tier is not live";

        /// <summary>Weighs nothing: the lane is not live, or the recipe has no container line.</summary>
        public static readonly ContainerWeighing Off = new(false, ContainerEras.Modern, new Dictionary<long, IReadOnlyList<ContainerSizeCandidate>>());

        public ContainerWeighing(bool live, string era, IReadOnlyDictionary<long, IReadOnlyList<ContainerSizeCandidate>> sizesByFood, bool otherEraLive = false)
        {
            Live = live;
            Era = era;
            OtherEraLive = otherEraLive;
            _sizes = sizesByFood;
        }

        public bool Live { get; }

        /// <summary>Whether the lane's old-book tier (<see cref="ContainerSizeService.OtherEraTier"/>) is live, so a line may take another era's size.</summary>
        public bool OtherEraLive { get; }

        /// <summary>The recipe's era (<see cref="ContainerEras.ForRecipe"/>).</summary>
        public string Era { get; }

        /// <summary>What a line in a container unit weighs, or null when the lane is not live or the line is not a measured container line.</summary>
        public ContainerWeight? Weigh(RecipeIngredientEntity ri) => Weigh(ri.Quantity, ri.Measurement, ri.IngredientId, ri.RawLine, ri.Ingredient);

        public ContainerWeight? Weigh(decimal quantity, MeasurementEntity? measurement, long ingredientId, string? rawLine, IngredientEntity? food)
        {
            if (!Live || measurement is not { IsContainer: true } || quantity <= 0) return null;
            var text = ContainerKinds.FromText(rawLine) != null
                ? rawLine
                : string.Create(CultureInfo.InvariantCulture, $"{quantity} {measurement.Name} {food?.Name}");
            var weight = ContainerSizeResolver.Resolve(text, Era, _sizes.GetValueOrDefault(ingredientId) ?? NoSizes, food?.Description, food?.GramsPerMilliliter,
                usdaCategoryId: food?.FdcCategoryId);
            return weight.Counted && !OtherEraLive && ContainerBasis.OtherEra(weight.Basis, Era)
                ? weight with { Grams = null, Reason = OtherEraNotLive }
                : weight;
        }

        /// <summary>The grams a measured container line weighs at its assumed size: the line's own count of containers times the weight of one; null when it is not weighed.</summary>
        public decimal? GramsFor(RecipeIngredientEntity ri) =>
            Weigh(ri) is { GramsPerContainer: { } each } ? Math.Round(ri.Quantity * (ri.Measurement!.BaseUnitConversionFactor ?? 1m) * each, 1) : null;

        /// <summary>Whether the container-sizes lane is live on the lane frame (its whole-lane row says live).</summary>
        public static Task<bool> LiveAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
            db.LaneStates.AsNoTracking().AnyAsync(s => s.Lane == AiLanes.ContainerSizes && s.Tier == LaneTiers.Whole && s.Mode == LaneMode.Live, cancellationToken);

        /// <summary>Whether the lane's old-book tier is live on the lane frame.</summary>
        public static Task<bool> OtherEraLiveAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
            db.LaneStates.AsNoTracking().AnyAsync(s => s.Lane == AiLanes.ContainerSizes && s.Tier == ContainerSizeService.OtherEraTier && s.Mode == LaneMode.Live, cancellationToken);

        /// <summary>The active container sizes the label may weigh with: every active row while the lane is live, none otherwise.</summary>
        public static IQueryable<IngredientContainerSizeEntity> WeighableSizes(ApplicationDbContext db) =>
            db.IngredientContainerSizes.Where(s => s.Status == ContainerSizeStatus.Active
                && db.LaneStates.Any(l => l.Lane == AiLanes.ContainerSizes && l.Tier == LaneTiers.Whole && l.Mode == LaneMode.Live));

        /// <summary>The weighing of one recipe's lines: its era and the active sizes of the foods on its measured container lines, when the lane is live.</summary>
        public static async Task<ContainerWeighing> ForRecipeAsync(ApplicationDbContext db, RecipeEntity recipe, IEnumerable<RecipeIngredientEntity>? lines = null, CancellationToken cancellationToken = default)
        {
            var foodIds = (lines ?? recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>())
                .Where(l => l.Quantity > 0 && l.Measurement?.IsContainer == true)
                .Select(l => l.IngredientId)
                .Distinct()
                .ToList();
            if (foodIds.Count == 0 || !await LiveAsync(db, cancellationToken)) return Off;

            var eras = string.IsNullOrWhiteSpace(recipe.SourceAttribution)
                ? new Dictionary<string, string>()
                : await db.RecipeSourceEras.AsNoTracking()
                    .Where(e => e.SourceAttribution == recipe.SourceAttribution)
                    .ToDictionaryAsync(e => e.SourceAttribution, e => e.Era, cancellationToken);
            var sizes = await db.IngredientContainerSizes.AsNoTracking()
                .Where(s => foodIds.Contains(s.IngredientId) && s.Status == ContainerSizeStatus.Active)
                .ToListAsync(cancellationToken);
            var byFood = sizes.GroupBy(s => s.IngredientId).ToDictionary(g => g.Key, g => (IReadOnlyList<ContainerSizeCandidate>)g
                .OrderBy(s => s.Id)
                .Select(s => new ContainerSizeCandidate(s.ContainerKind, s.Era, s.SizeWord, s.NetGrams, s.DrainedGrams, s.SourceRef, s.Status, s.Id))
                .ToList());
            var era = ContainerEras.ForRecipe(recipe.SourceSite, recipe.SourceAttribution, eras);
            return new ContainerWeighing(true, era, byFood, era != ContainerEras.Modern && await OtherEraLiveAsync(db, cancellationToken));
        }

        /// <summary>
        /// The text a cook sees for an assumed size: one container's weight, drained when it is, and
        /// where the size comes from ("1 can ≈ 15 oz (425 g): the most common size in USDA branded
        /// data (2025-12-18, 223 of 304 products); estimated").
        /// </summary>
        public static string Describe(ContainerWeight weight)
        {
            var each = weight.GramsPerContainer ?? 0m;
            var kind = weight.Kind ?? ContainerKinds.Package;
            var head = weight.Drained
                ? string.Create(CultureInfo.InvariantCulture, $"1 {kind}, drained ≈ {each:0} g")
                : string.Create(CultureInfo.InvariantCulture, $"1 {kind} ≈ {each / CanSizeTable.GramsPerOunce:0.##} oz ({each:0} g)");
            var source = Source(weight.SourceRef);
            var why = weight.Basis switch
            {
                ContainerBasis.Stated => weight.Drained ? "the size the line states, drained by the food's drained share" : "the size the line states",
                ContainerBasis.Numbered => "the numbered can in " + source,
                ContainerBasis.PeriodSize => source + ", the size of the book's time",
                ContainerBasis.PeriodNearer => "of the two can sizes " + source + ", the one nearer today's most common size",
                ContainerBasis.ModernFallback => source + "; today's size, since no size of the book's time is on record",
                _ => source,
            };
            return head + ": " + why + "; estimated";
        }

        private static string Source(string? sourceRef)
        {
            var first = (sourceRef ?? string.Empty).Split('|')[0];
            if (first.StartsWith("fdc:", StringComparison.Ordinal))
            {
                var parts = first.Split(':');
                return parts.Length > 1 ? $"the USDA portion for this food (FDC {parts[1]})" : "a USDA portion for this food";
            }
            if (first.StartsWith("deterministic:fdc-branded-mode:", StringComparison.Ordinal))
            {
                var parts = first["deterministic:fdc-branded-mode:".Length..].Split(':');
                return parts.Length >= 3
                    ? $"the most common size in USDA branded data ({parts[0]}, {parts[2].Replace("/", " of ", StringComparison.Ordinal)} products)"
                    : "the most common size in USDA branded data";
            }
            if (first.StartsWith("deterministic:standard-of-fill:", StringComparison.Ordinal))
                return "the federal standard of fill (" + first["deterministic:standard-of-fill:".Length..] + ")";
            if (first.StartsWith("deterministic:can-table:", StringComparison.Ordinal))
            {
                var ids = first.Split('+').Select(r => r.StartsWith("deterministic:can-table:", StringComparison.Ordinal) ? r["deterministic:can-table:".Length..] : r).ToHashSet(StringComparer.Ordinal);
                var rows = CanSizeTable.Rows.Where(r => ids.Contains(r.Id)).ToList();
                var period = rows.FirstOrDefault(r => r.Candidates != null);
                if (period != null) return $"a {period.Publisher.Split(',')[0]} source of {period.Year} names for vegetables (No. {string.Join(" or No. ", period.CandidateNumbers)})";
                return rows.Count == 0
                    ? "the cited can table"
                    : "the cited can table (" + string.Join("; ", rows.Select(r => $"{r.Name}, {r.Year}")) + ")";
            }
            return "a cited source";
        }
    }
}
