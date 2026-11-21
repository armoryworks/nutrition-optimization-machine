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
    /// line of an old or undated book sized by one of the lane's old-book tiers (the same book's
    /// sized lines, the same era's, a cited period source, today's size; <see cref="ContainerBasis.OldBookTier"/>)
    /// takes the size of the first of those tiers that is live, and stays uncounted only when no
    /// live tier gives one. A line without a container word
    /// in its text is read by its unit ("1 Jar" of the food).
    /// Every weight it gives is an estimate and is shown as one.
    /// </summary>
    public sealed class ContainerWeighing
    {
        private static readonly IReadOnlyList<ContainerSizeCandidate> NoSizes = Array.Empty<ContainerSizeCandidate>();

        private readonly IReadOnlyDictionary<long, IReadOnlyList<ContainerSizeCandidate>> _sizes;

        public const string OtherEraNotLive = "an old or undated book's size only by tiers of the lane that are not live";

        /// <summary>Weighs nothing: the lane is not live, or the recipe has no container line.</summary>
        public static readonly ContainerWeighing Off = new(false, ContainerEras.Modern, new Dictionary<long, IReadOnlyList<ContainerSizeCandidate>>());

        public ContainerWeighing(bool live, string era, IReadOnlyDictionary<long, IReadOnlyList<ContainerSizeCandidate>> sizesByFood, IEnumerable<string>? liveTiers = null, string? book = null)
        {
            Live = live;
            Era = era;
            LiveTiers = (liveTiers ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            Book = book;
            _sizes = sizesByFood;
        }

        public bool Live { get; }

        /// <summary>The lane's old-book tiers (<see cref="ContainerSizeService.OldBookTiers"/>) that are live, so a line may take the size they give.</summary>
        public IReadOnlySet<string> LiveTiers { get; }

        /// <summary>The recipe's book (<see cref="ContainerSizeService.BookOf"/>), whose own sized lines give the same-book tier's sizes; null for a web recipe.</summary>
        public string? Book { get; }

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
            var sizes = _sizes.GetValueOrDefault(ingredientId) ?? NoSizes;
            var skip = Era == ContainerEras.Modern ? null : ContainerSizeService.OldBookTiers.Where(t => !LiveTiers.Contains(t)).ToHashSet(StringComparer.Ordinal);
            var weight = ContainerSizeResolver.Resolve(text, Era, sizes, food?.Description, food?.GramsPerMilliliter,
                usdaCategoryId: food?.FdcCategoryId, book: Book, skipTiers: skip);
            if (weight.Counted || skip is not { Count: > 0 }) return weight;
            var unskipped = ContainerSizeResolver.Resolve(text, Era, sizes, food?.Description, food?.GramsPerMilliliter,
                usdaCategoryId: food?.FdcCategoryId, book: Book);
            return unskipped.Counted ? unskipped with { Grams = null, Reason = OtherEraNotLive } : weight;
        }

        /// <summary>The grams a measured container line weighs at its assumed size: the line's own count of containers times the weight of one; null when it is not weighed.</summary>
        public decimal? GramsFor(RecipeIngredientEntity ri) =>
            Weigh(ri) is { GramsPerContainer: { } each } ? Math.Round(ri.Quantity * (ri.Measurement!.BaseUnitConversionFactor ?? 1m) * each, 1) : null;

        /// <summary>Whether the container-sizes lane is live on the lane frame (its whole-lane row says live).</summary>
        public static Task<bool> LiveAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
            db.LaneStates.AsNoTracking().AnyAsync(s => s.Lane == AiLanes.ContainerSizes && s.Tier == LaneTiers.Whole && s.Mode == LaneMode.Live, cancellationToken);

        /// <summary>The lane's old-book tiers that are live on the lane frame.</summary>
        public static async Task<IReadOnlyList<string>> LiveTiersAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
        {
            var tiers = ContainerSizeService.OldBookTiers.ToList();
            return await db.LaneStates.AsNoTracking()
                .Where(s => s.Lane == AiLanes.ContainerSizes && tiers.Contains(s.Tier) && s.Mode == LaneMode.Live)
                .Select(s => s.Tier)
                .ToListAsync(cancellationToken);
        }

        /// <summary>The active container sizes the label may weigh with: every active row while the lane is live, none otherwise.</summary>
        public static IQueryable<IngredientContainerSizeEntity> WeighableSizes(ApplicationDbContext db) =>
            db.IngredientContainerSizes.Where(s => s.Status == ContainerSizeStatus.Active
                && db.LaneStates.Any(l => l.Lane == AiLanes.ContainerSizes && l.Tier == LaneTiers.Whole && l.Mode == LaneMode.Live));

        /// <summary>The weighing of one recipe's lines: its era, its book and the active sizes of the foods on its measured container lines (of the corpus's book sizes, only its own book's), when the lane is live.</summary>
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
            var book = ContainerSizeService.BookOf(recipe.SourceSite, recipe.SourceAttribution);
            var sizes = await db.IngredientContainerSizes.AsNoTracking()
                .Where(s => foodIds.Contains(s.IngredientId) && s.Status == ContainerSizeStatus.Active && (s.SourceAttribution == null || s.SourceAttribution == book))
                .ToListAsync(cancellationToken);
            var byFood = sizes.GroupBy(s => s.IngredientId).ToDictionary(g => g.Key, g => (IReadOnlyList<ContainerSizeCandidate>)g
                .OrderBy(s => s.Id)
                .Select(s => new ContainerSizeCandidate(s.ContainerKind, s.Era, s.SizeWord, s.NetGrams, s.DrainedGrams, s.SourceRef, s.Status, s.Id, s.SourceAttribution))
                .ToList());
            var era = ContainerEras.ForRecipe(recipe.SourceSite, recipe.SourceAttribution, eras);
            return new ContainerWeighing(true, era, byFood, era != ContainerEras.Modern ? await LiveTiersAsync(db, cancellationToken) : null, book);
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
                ContainerBasis.SameBook => source,
                ContainerBasis.SameEra => source,
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
            if (ContainerCorpusSizes.TierOf(first) is { } tier)
            {
                var parts = first.Split(':');
                var held = parts.Length >= 6 ? parts[5].Split('/') : Array.Empty<string>();
                var spread = parts.Length >= 7 ? parts[6] : null;
                var books = parts.Length >= 4 && parts[3] == ContainerEras.Unknown ? "other undated books" : "books of its time";
                if (held.Length != 2) return tier == ContainerCorpusSizes.BookTier ? "the size this book states elsewhere" : $"the size {books} state";
                return tier == ContainerCorpusSizes.BookTier
                    ? $"the size this book states for it elsewhere ({held[0]} of {held[1]} sized lines)"
                    : $"the most common size {books} state ({held[0]} of {held[1]} books{(spread != null ? ", spread " + spread : string.Empty)})";
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
