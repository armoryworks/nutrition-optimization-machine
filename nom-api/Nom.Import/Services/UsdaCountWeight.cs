using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Import.Services
{
    /// <summary>The weight of one countable item and the USDA portion it came from.</summary>
    public sealed record UsdaCountPortion(decimal Grams, string Portion);

    /// <summary>
    /// Derives the weight of one item ("1 clove", "1 medium onion", "1 stalk") from a food's USDA
    /// household portions, so a recipe line counted in pieces has a mass. Only discrete items count:
    /// a portion whose unit is a piece word (clove, each, piece, fruit, stalk, …), a bare size
    /// ("medium", "large"), or the food's own name ("1 cucumber" for Cucumber, with peel, raw).
    /// Wholes that hold many pieces (head, bulb, bunch, loaf, pie) and fractions (slice, wedge, strip)
    /// never count, so garlic resolves to a clove rather than a bulb.
    /// The size the food's description names wins, then medium, then an unsized item, then large,
    /// small and the extremes; a single-item portion beats a multi-item one, and file order breaks
    /// ties. A multi-item portion ("3 cloves = 9 g") is divided by its amount.
    /// </summary>
    public static class UsdaCountWeight
    {
        private const decimal MaxGrams = 5000m;

        private static readonly HashSet<string> PieceWords = new(StringComparer.Ordinal)
        {
            "clove", "cloves", "each", "piece", "pieces", "fruit", "stalk", "stalks", "item", "unit",
            "whole", "egg", "eggs", "ear", "spear", "spears", "pod", "pods", "sprig", "sprigs",
            "link", "links", "patty", "patties", "fillet", "filet", "breast",
            "thigh", "drumstick", "wing", "leg", "chop",
        };

        private static readonly HashSet<string> SizeWords = new(StringComparer.Ordinal)
        {
            "medium", "large", "small", "extra", "jumbo", "mini", "miniature",
        };

        private static readonly HashSet<string> NeverOneItem = new(StringComparer.Ordinal)
        {
            "head", "bulb", "bunch", "loaf", "rack", "bird", "chicken", "roast", "pie", "pizza", "cake",
            "slice", "slices", "wedge", "strip", "strips", "half", "cubic", "inch",
            "oz", "lb", "g", "kg", "serving", "nlea", "package", "container", "can", "jar",
            "bottle", "box", "bag", "packet", "carton", "envelope", "recipe", "portion",
        };

        private static readonly Regex Disqualifier = new(
            @"\b(yields?|as purchased|chopped|sliced|diced|cubed|shredded|grated|mashed|pureed|halves|wedges?|slices?|strips?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Words = new(@"[a-z]+", RegexOptions.Compiled);

        public static UsdaCountPortion? GramsPerCount(IEnumerable<UsdaPortion> portions, string? foodDescription)
        {
            var food = (foodDescription ?? string.Empty).ToLowerInvariant();
            var foodWords = Words.Matches(food).Select(m => Singular(m.Value)).ToHashSet(StringComparer.Ordinal);
            var namedSize = SizeOf(food);

            var best = portions
                .Select((p, order) => (Portion: p, Order: order, Text: CountText(p, foodWords)))
                .Where(x => x.Text != null && x.Portion.Amount > 0 && x.Portion.Grams > 0)
                .Select(x => (x.Portion, x.Order, x.Text, Size: SizeOf(x.Text!), Grams: x.Portion.Grams / x.Portion.Amount))
                .Where(x => x.Grams <= MaxGrams)
                .OrderBy(x => namedSize != null && x.Size == namedSize ? 0 : 1)
                .ThenBy(x => SizeRank(x.Size))
                .ThenBy(x => x.Portion.Amount == 1m ? 0 : 1)
                .ThenBy(x => x.Order)
                .FirstOrDefault();
            if (best.Text == null) return null;

            var grams = Math.Round(best.Grams, 2);
            return grams > 0 ? new UsdaCountPortion(grams, $"{best.Portion.Amount:0.###} {best.Text}") : null;
        }

        private static string? CountText(UsdaPortion p, HashSet<string> foodWords)
        {
            if (UsdaVolumeDensity.IsVolume(p)) return null;

            var unitName = (p.MeasureUnitName ?? string.Empty).Trim().ToLowerInvariant();
            var modifier = (p.Modifier ?? string.Empty).Trim().ToLowerInvariant();
            var description = (p.Description ?? string.Empty).Trim().ToLowerInvariant();
            var named = unitName.Length > 0 && unitName != "undetermined";

            var text = named
                ? string.Join(' ', new[] { unitName, modifier, description }.Where(t => t.Length > 0))
                : (modifier.Length > 0 ? modifier : description);
            if (text.Length == 0 || Disqualifier.IsMatch(text)) return null;

            var unit = named ? unitName : Words.Match(text).Value;
            if (unit.Length == 0 || NeverOneItem.Contains(unit)) return null;

            return PieceWords.Contains(unit) || SizeWords.Contains(unit) || foodWords.Contains(Singular(unit))
                ? text
                : null;
        }

        private static string? SizeOf(string text)
        {
            if (Regex.IsMatch(text, @"\b(extra[- ]large|jumbo|mini|miniature)\b")) return "extreme";
            if (Regex.IsMatch(text, @"\bmedium\b")) return "medium";
            if (Regex.IsMatch(text, @"\blarge\b")) return "large";
            if (Regex.IsMatch(text, @"\bsmall\b")) return "small";
            return null;
        }

        private static int SizeRank(string? size) => size switch
        {
            "medium" => 0,
            null => 1,
            "large" => 2,
            "small" => 3,
            _ => 4,
        };

        private static string Singular(string word)
        {
            if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal)) return word[..^3] + "y";
            if (word.Length > 4 && word.EndsWith("oes", StringComparison.Ordinal)) return word[..^2];
            if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)) return word[..^1];
            return word;
        }
    }
}
