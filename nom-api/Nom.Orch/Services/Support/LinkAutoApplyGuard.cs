using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Deterministic reasons to keep a name match or a model pick for a person instead of applying
    /// it on its own. Each rule comes from a class of wrong links found in the judged set: a wording
    /// that names more than one food, a wording that is the name of another curated food, a USDA
    /// food in a state (dried, canned, cooked...) the wording does not mention while the catalog has
    /// other foods under the same lead word, and, for name matches, a wording that reads in the USDA
    /// description's own order ("cake chocolate", "gravy beef"). A hold only withholds auto-apply.
    /// </summary>
    public static class LinkAutoApplyGuard
    {
        public const string HoldMark = " · hold: ";
        public const string CompoundRule = "more than one food";
        public const string OtherFoodRule = "names another curated food";
        public const string StateRule = "USDA state not in the wording";
        public const string WordOrderRule = "USDA word order";
        public const string UndoneRule = "undone";

        private static readonly Regex Word = new(@"[\p{L}']+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> Joiners = new(StringComparer.Ordinal) { "and", "or", "with", "&", "+", "n'" };

        private static readonly HashSet<string> Notes = new(StringComparer.Ordinal)
        {
            "chopped", "minced", "diced", "sliced", "grated", "shredded", "melted", "softened", "beaten", "sifted", "peeled", "pared",
            "cored", "cubed", "halved", "quartered", "divided", "drained", "rinsed", "trimmed", "juiced", "zested", "crushed", "mashed",
            "toasted", "packed", "firmly", "loosely", "finely", "coarsely", "roughly", "thinly", "freshly", "lightly", "well", "very",
            "cut", "into", "in", "pieces", "piece", "chunks", "strips", "cubes", "rings", "wedges", "small", "large", "medium", "fine",
            "thin", "thick", "fresh", "room", "temperature", "at", "cold", "warm", "hot", "optional", "taste", "to", "for", "serving",
            "garnish", "about", "inch", "inches", "removed", "stems", "stemmed", "seeds", "seeded", "pitted", "deveined", "shelled",
            "skinned", "boneless", "skinless", "washed", "cleaned", "plus", "more", "as", "needed", "if", "desired", "the", "a", "an",
            "of", "each", "whole", "raw", "uncooked", "ripe", "sweetened", "unsweetened", "salted", "unsalted", "only", "separated",
            "lengthwise", "crosswise", "torn", "broken", "thawed", "chilled", "warmed", "heated", "kept", "reserved", "dusting",
        };

        private static readonly Dictionary<string, string[]> States = new(StringComparer.Ordinal)
        {
            ["dried"] = new[] { "dried", "dry", "dehydrated" },
            ["dehydrated"] = new[] { "dried", "dry", "dehydrated" },
            ["canned"] = new[] { "canned", "can", "cans", "tinned", "tin", "jarred" },
            ["cooked"] = new[] { "cooked", "boiled", "baked", "roasted", "fried", "steamed", "braised", "stewed", "grilled", "leftover" },
            ["boiled"] = new[] { "cooked", "boiled" },
            ["frozen"] = new[] { "frozen" },
            ["ground"] = new[] { "ground", "minced" },
            ["powder"] = new[] { "powder", "powdered", "confectioners", "confectioner's", "icing" },
            ["powdered"] = new[] { "powder", "powdered", "confectioners", "confectioner's", "icing" },
        };

        private static readonly HashSet<string> ClassWords = new(StringComparer.Ordinal)
        {
            "spice", "spices", "oil", "oils", "leavening", "agent", "agents", "seasoning", "condiment", "condiments", "sweetener", "sweeteners",
            "nut", "nuts", "seed", "seeds", "sauce", "sauces", "vinegar", "salad", "dressing", "cereal", "cereals", "beverage", "beverages",
            "alcoholic", "fish", "crustacean", "crustaceans", "mollusk", "mollusks",
        };

        /// <summary>What the rules need to know about the catalog, built once per pass.</summary>
        public sealed class Catalog
        {
            /// <summary>Curated foods with nutrition, by name, as <see cref="LineRelinkService.TargetFor"/> reads them.</summary>
            public IReadOnlyDictionary<string, long> Curated { get; init; } = new Dictionary<string, long>();
            public IReadOnlyDictionary<long, string> CuratedNames { get; init; } = new Dictionary<long, string>();
            /// <summary>How many USDA foods have each lead word (the first word of the description).</summary>
            public IReadOnlyDictionary<string, int> LeadWords { get; init; } = new Dictionary<string, int>();
        }

        /// <summary>One pick as the rules read it.</summary>
        public sealed record Pick(string Wording, long SourceId, long TargetId, string TargetName, string Description, bool NameMatch);

        /// <summary>The first rule that holds the pick, with its detail, or null to let it through.</summary>
        public static string? Hold(Pick pick, Catalog catalog)
        {
            var wording = IngredientNameNormalizer.Normalize(pick.Wording);
            if (IsCompound(wording)) return CompoundRule;

            if (LineRelinkService.TargetFor(wording, pick.TargetId, pick.TargetName.Trim(), catalog.Curated) is { } other && other != pick.SourceId)
            {
                return $"{OtherFoodRule} ({(catalog.CuratedNames.TryGetValue(other, out var name) ? name : "#" + other)})";
            }

            var words = Words(wording);
            var description = Words(pick.Description);
            if (description.Count > 0 && catalog.LeadWords.TryGetValue(description[0], out var siblings) && siblings > 1)
            {
                foreach (var state in description.Where(States.ContainsKey).Distinct())
                {
                    if (!States[state].Any(words.Contains)) return $"{StateRule} ({state})";
                }
            }

            if (pick.NameMatch && FollowsDescriptionOrder(words, pick.Description)) return WordOrderRule;
            return null;
        }

        /// <summary>
        /// The reason of a link an undo took back: held for a person, so no lane applies the same
        /// pick again on its own.
        /// </summary>
        public static string MarkUndone(string? reason)
        {
            var mark = HoldMark + UndoneRule;
            if (reason != null && reason.EndsWith(mark, StringComparison.Ordinal)) return reason;
            var marked = (reason ?? string.Empty) + mark;
            return marked.Length <= 2047 ? marked : marked[..(2047 - mark.Length)] + mark;
        }

        public static bool IsMarked(string? reason) => reason != null && reason.Contains(HoldMark, StringComparison.Ordinal);

        internal static bool IsCompound(string wording)
        {
            if (wording.IndexOfAny(new[] { '+', '&', ';' }) >= 0) return true;
            var segments = wording.ToLowerInvariant().Split(',');
            if (segments.Any(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(Joiners.Contains))) return true;
            return segments.Skip(1).Any(s => Words(s).Any(w => !Notes.Contains(w)));
        }

        /// <summary>
        /// True when the wording's first identifying word comes from the description's lead segment
        /// and a later word from one of its qualifying segments, the order USDA writes and a cook
        /// does not ("chocolate cake" reads right for "Cake, chocolate"; "cake chocolate" does not).
        /// </summary>
        internal static bool FollowsDescriptionOrder(IReadOnlyList<string> words, string description)
        {
            var segments = description.Split(',').Select(s => Words(s).Select(Singular).ToHashSet(StringComparer.Ordinal)).ToList();
            if (segments.Count < 2) return false;
            var lead = segments[0].Count > 0 && segments[0].All(ClassWords.Contains) && segments.Count > 2 ? 1 : 0;
            var head = segments[lead];
            var later = segments.Skip(lead + 1).SelectMany(s => s).Where(w => !head.Contains(w)).ToHashSet(StringComparer.Ordinal);
            var singular = words.Select(Singular).ToList();
            var headAt = singular.FindIndex(head.Contains);
            var laterAt = singular.FindLastIndex(later.Contains);
            return headAt >= 0 && laterAt > headAt;
        }

        internal static List<string> Words(string text) =>
            Word.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();

        private static string Singular(string word) =>
            word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
            : word.Length > 4 && word.EndsWith("oes", StringComparison.Ordinal) ? word[..^2]
            : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1]
            : word;
    }
}
