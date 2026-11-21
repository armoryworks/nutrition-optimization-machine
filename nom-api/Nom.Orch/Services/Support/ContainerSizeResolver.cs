using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>One container size of a food as the resolver reads it (a row of nutrient.IngredientContainerSize, or a size being scored).</summary>
    public sealed record ContainerSizeCandidate(
        string Kind,
        string Era,
        string SizeWord,
        decimal NetGrams,
        decimal? DrainedGrams,
        string SourceRef,
        string Status = ContainerSizeStatus.Active,
        long RowId = 0);

    /// <summary>What a container line weighs: the grams of all its containers, whether drained, where the size came from (<see cref="ContainerBasis"/>), or why it has none.</summary>
    public sealed record ContainerWeight(decimal? Grams, decimal Count, string? Kind, bool Drained, string? SourceRef, string? Reason, long RowId = 0, string? Basis = null)
    {
        public bool Counted => Grams is > 0;

        public decimal? GramsPerContainer => Grams is > 0 && Count > 0 ? Grams / Count : null;
    }

    /// <summary>How a container line's size was found, for the line's text and for the hold-out, which scores a key only on lines of its own era.</summary>
    public static class ContainerBasis
    {
        public const string Stated = "stated";
        public const string Numbered = "numbered";
        public const string FoodSize = "food-size";
        public const string AnyEra = "any-era";
        public const string PeriodSize = "period-size";
        public const string PeriodNearer = "period-nearer";
        public const string ModernFallback = "modern-fallback";

        /// <summary>Whether the size is the key's own answer for the line's era (the hold-out scores a key on these only).</summary>
        public static bool OwnEra(string? basis) => basis is FoodSize or AnyEra or PeriodSize;
    }

    /// <summary>
    /// The size a container line is weighed at, in order: the size the line states, the numbered can
    /// it names (the cited can table, by the recipe's era), else the food's own container size of
    /// that kind, size word and era. A line that says drained, or a food whose USDA description is
    /// drained solids, is weighed drained: by the size's drained weight, scaled to a stated net size;
    /// with no drained weight it is not weighed, and a drained line on a food whose USDA description
    /// is the food as packed is not weighed either (it needs the drained food). An unsized line in an
    /// old (historic) or undated (unknown) book takes a size that holds in any era, else the food's
    /// historic size, else the size a period row of the can table names for the food's USDA category
    /// (of two candidate cans, the one nearer the food's modern size, when that modern size lies
    /// within the candidates' span, see <see cref="PeriodCovers"/>; with no modern size, none), else
    /// the food's modern size; such a line is always shown as estimated. The hold-out hides the
    /// stated size to ask what the lane would have assumed.
    /// </summary>
    public static class ContainerSizeResolver
    {
        public const string NoContainer = "no container word";
        public const string NoSize = "no size for this food, kind and era";
        public const string NoDrained = "drained, and no drained weight is known";
        public const string StatedInVolume = "the stated size is a volume and the food has no density";
        public const string PeriodWithoutModern = "a period source names two can sizes for this food and no modern size chooses between them";
        public const string Stated = "stated";
        public const string AsPacked = "drained line on a food weighed as packed; the line needs the drained food (form relink)";

        private static readonly Dictionary<string, decimal> GramsPerUnit = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ounce"] = CanSizeTable.GramsPerOunce,
            ["Pound"] = 453.59237m,
            ["Gram"] = 1m,
            ["Kilogram"] = 1000m,
        };

        private static readonly Dictionary<string, decimal> MillilitersPerUnit = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Milliliter"] = 1m,
            ["Liter"] = 1000m,
            ["Cup"] = 236.5882365m,
            ["Tablespoon"] = 14.78676478m,
            ["Teaspoon"] = 4.928921594m,
        };

        public static ContainerWeight Resolve(
            string? rawLine,
            string era,
            IEnumerable<ContainerSizeCandidate> sizes,
            string? usdaDescription = null,
            decimal? gramsPerMilliliter = null,
            bool hideStatedSize = false,
            bool activeOnly = true,
            bool netOnly = false,
            int? usdaCategoryId = null)
        {
            var kind = ContainerKinds.FromText(rawLine);
            if (kind == null) return new ContainerWeight(null, 0, null, false, null, NoContainer);
            var count = Count(rawLine);
            var drained = !netOnly && (ContainerKinds.SaysDrained(rawLine) || ContainerKinds.DescribesDrained(usdaDescription));
            if (!netOnly && usdaDescription != null && ContainerKinds.SaysDrained(rawLine) && !ContainerKinds.DescribesDrained(usdaDescription))
                return new ContainerWeight(null, count, kind, true, null, AsPacked);
            var sizeWord = ContainerKinds.SizeWord(rawLine);
            var usable = sizes.Where(s => s.Kind == kind && (!activeOnly || s.Status == ContainerSizeStatus.Active)).ToList();

            if (!hideStatedSize && StatedPerContainer(rawLine, gramsPerMilliliter) is { } stated)
            {
                if (stated.Reason != null) return new ContainerWeight(null, count, kind, drained, null, stated.Reason);
                return Weigh(stated.Grams, stated.SourceRef, count, kind, drained, usable, 0, ContainerBasis.Stated);
            }

            if (!hideStatedSize && IngredientLineParser.CanNumber(rawLine) is { } number)
            {
                return CanSizeTable.Numbered(number, era) is { } numbered
                    ? Weigh(numbered.Grams, numbered.SourceRef, count, kind, drained, usable, 0, ContainerBasis.Numbered)
                    : new ContainerWeight(null, count, kind, drained, null, $"the can table gives no size for No. {number} in era {era}");
            }

            var keyed = usable.Where(s => s.SizeWord == sizeWord).ToList();
            var any = keyed.FirstOrDefault(s => s.Era == ContainerEras.Any);
            var modern = keyed.FirstOrDefault(s => s.Era == ContainerEras.Modern);
            ContainerSizeCandidate? pick;
            string basis;
            if (era == ContainerEras.Modern)
            {
                pick = modern ?? any;
                basis = modern != null ? ContainerBasis.FoodSize : ContainerBasis.AnyEra;
            }
            else if (any != null)
            {
                pick = any;
                basis = ContainerBasis.AnyEra;
            }
            else if (keyed.FirstOrDefault(s => s.Era == ContainerEras.Historic) is { } historic)
            {
                pick = historic;
                basis = ContainerBasis.PeriodSize;
            }
            else if (CanSizeTable.Period(kind, sizeWord, usdaCategoryId) is { Count: > 0 } period && (modern == null || PeriodCovers(period, modern.NetGrams)))
            {
                if (modern == null) return new ContainerWeight(null, count, kind, drained, null, PeriodWithoutModern);
                var nearer = period.OrderBy(c => Math.Abs(c.Grams - modern.NetGrams)).ThenBy(c => c.Grams).First();
                return Weigh(nearer.Grams, nearer.SourceRef + "|nearer:" + modern.SourceRef, count, kind, drained, usable, modern.RowId, ContainerBasis.PeriodNearer);
            }
            else
            {
                pick = modern;
                basis = ContainerBasis.ModernFallback;
            }
            if (pick == null) return new ContainerWeight(null, count, kind, drained, null, NoSize);
            if (!drained) return new ContainerWeight(Math.Round(pick.NetGrams * count, 1), count, kind, false, pick.SourceRef, null, pick.RowId, basis);
            return pick.DrainedGrams is > 0
                ? new ContainerWeight(Math.Round(pick.DrainedGrams.Value * count, 1), count, kind, true, pick.SourceRef, null, pick.RowId, basis)
                : new ContainerWeight(null, count, kind, true, pick.SourceRef, NoDrained, pick.RowId, basis);
        }

        /// <summary>
        /// Whether the period sizes speak of a food whose modern container is this big: its modern
        /// size lies between the smallest and the largest candidate, widened by the 10 % agreement
        /// tolerance. A food sold today in a much smaller or larger can (tomato paste at 6 oz, a
        /// 28 oz can of whole tomatoes) was not one of the medium cans the period source names.
        /// </summary>
        public static bool PeriodCovers(IReadOnlyList<PeriodCandidate> period, decimal modernNetGrams) =>
            modernNetGrams >= period.Min(c => c.Grams) * (1 - ContainerSizeAgreement.Tolerance)
            && modernNetGrams <= period.Max(c => c.Grams) * (1 + ContainerSizeAgreement.Tolerance);

        private static ContainerWeight Weigh(decimal netGrams, string source, decimal count, string kind, bool drained, IReadOnlyList<ContainerSizeCandidate> usable, long rowId, string basis)
        {
            if (!drained) return new ContainerWeight(Math.Round(netGrams * count, 1), count, kind, false, source, null, rowId, basis);
            var reference = usable.FirstOrDefault(s => s.DrainedGrams is > 0 && s.NetGrams > 0);
            if (reference == null) return new ContainerWeight(null, count, kind, true, source, NoDrained, rowId, basis);
            var grams = netGrams * reference.DrainedGrams!.Value / reference.NetGrams;
            return new ContainerWeight(Math.Round(grams * count, 1), count, kind, true, source + "|drained:" + reference.SourceRef, null, rowId == 0 ? reference.RowId : rowId, basis);
        }

        /// <summary>The number of containers the line counts ("2 (15 oz) cans" → 2, "1 can" → 1); 1 when it gives none.</summary>
        public static decimal Count(string? rawLine)
        {
            if (IngredientLineParser.SizedContainers(rawLine) is { Quantity: > 0 } sized) return sized.Quantity;
            if (IngredientLineParser.Parse(rawLine) is { Quantity: > 0 } parsed
                && IngredientLineParser.IsContainer(parsed.MeasurementName)) return parsed.Quantity;
            return 1m;
        }

        /// <summary>The size the line states for one container, in grams; a reason when it states a volume the food has no density for; null when it states none.</summary>
        public static (decimal Grams, string SourceRef, string? Reason)? StatedPerContainer(string? rawLine, decimal? gramsPerMilliliter)
        {
            if (IngredientLineParser.StatedSize(rawLine) is not { Quantity: > 0 } stated) return null;
            var count = IngredientLineParser.SizedContainers(rawLine) is { Quantity: > 0 } c ? c.Quantity : 1m;
            decimal grams;
            if (GramsPerUnit.TryGetValue(stated.MeasurementName, out var perUnit)) grams = stated.Quantity * perUnit;
            else if (MillilitersPerUnit.TryGetValue(stated.MeasurementName, out var ml))
            {
                if (gramsPerMilliliter is not > 0) return (0, Stated, StatedInVolume);
                grams = stated.Quantity * ml * gramsPerMilliliter.Value;
            }
            else return null;
            return (Math.Round(grams / count, 1), Stated, null);
        }
    }
}
