using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The hold mark a held name match or model pick carries in its reason, the names of the hold
    /// rules a pass has always reported, and the compound test. The rules themselves are the link
    /// gate's (<see cref="LinkGate"/>); a hold only withholds auto-apply.
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

        internal static readonly HashSet<string> Joiners = new(StringComparer.Ordinal) { "and", "or", "with", "&", "+", "n'" };

        internal static readonly HashSet<string> Notes = new(StringComparer.Ordinal)
        {
            "chopped", "minced", "diced", "sliced", "grated", "shredded", "melted", "softened", "beaten", "sifted", "peeled", "pared",
            "cored", "cubed", "halved", "quartered", "divided", "drained", "rinsed", "trimmed", "juiced", "zested", "crushed", "mashed",
            "toasted", "packed", "firmly", "loosely", "finely", "coarsely", "roughly", "thinly", "freshly", "lightly", "well", "very",
            "cut", "into", "in", "pieces", "piece", "chunks", "strips", "cubes", "rings", "wedges", "small", "large", "medium", "fine",
            "thin", "thick", "fresh", "room", "temperature", "at", "cold", "warm", "hot", "optional", "taste", "to", "for", "serving",
            "garnish", "about", "inch", "inches", "removed", "stems", "stemmed", "seeds", "seeded", "pitted", "deveined", "shelled",
            "skinned", "boneless", "skinless", "washed", "cleaned", "plus", "more", "as", "needed", "if", "desired", "the", "a", "an",
            "of", "each", "whole", "raw", "uncooked", "ripe", "sweetened", "unsweetened", "salted", "unsalted", "only", "separated",
            "lengthwise", "crosswise", "cracked", "torn", "broken", "thawed", "chilled", "warmed", "heated", "kept", "reserved", "dusting",
        };

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

        /// <summary>Words a preparation note may hold beside <see cref="Notes"/>: a joiner between two of them ("minced or grated", "melted and slightly cooled") joins two steps of one food, not two foods.</summary>
        internal static readonly HashSet<string> PrepWords = new(StringComparer.Ordinal)
        {
            "cooled", "cooked", "stewed", "strained", "slightly", "roasted", "boiled", "steamed", "blanched", "sauteed", "scrubbed", "patted", "dry",
            "dried", "squeezed", "picked", "over", "through", "sorted", "soaked", "sifted", "skins", "tails", "on", "off", "out", "down", "up", "then",
        };

        /// <summary>Names of one food that hold a joiner ("half and half", "lea &amp; perrins'"): never two foods.</summary>
        internal static readonly string[] OneFoodNames =
        {
            "half and half", "half & half", "half-and-half", "lea & perrins", "lea and perrins", "macaroni and cheese", "mac and cheese", "mac & cheese",
            "sweet and sour", "pork and beans",
        };

        /// <summary>
        /// Whether a wording names more than one food: a joiner ("and", "or", "with", "&amp;", "+")
        /// between two foods, or a comma segment after the food that holds a food word. A joiner
        /// between two preparation words ("garlic, minced or grated", "chickpeas, drained and
        /// rinsed", "stewed and strained tomatoes"), a comma after leading notes ("boneless, skinless
        /// chicken breasts") and a one-food name with a joiner ("half and half") are one food.
        /// </summary>
        internal static bool IsCompound(string wording)
        {
            var text = wording.ToLowerInvariant();
            foreach (var name in OneFoodNames) text = text.Replace(name, name.Replace(' ', '_').Replace("&", "_").Replace('-', '_'));
            if (text.IndexOfAny(new[] { '+', '&', ';' }) >= 0) return true;
            var segments = text.Split(',').ToList();
            while (segments.Count > 1 && Words(segments[0]).Count > 0 && Words(segments[0]).All(IsPrep)) segments.RemoveAt(0);
            foreach (var segment in segments)
            {
                var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ':', '(', ')')).ToList();
                for (var i = 0; i < words.Count; i++)
                {
                    if (!Joiners.Contains(words[i])) continue;
                    var before = words.Take(i).LastOrDefault(w => w != "slightly" && w != "very");
                    var after = words.Skip(i + 1).FirstOrDefault(w => w != "slightly" && w != "very");
                    if (before == null || after == null || !IsPrep(before) || !IsPrep(after) || words[i] == "with") return true;
                }
            }
            return segments.Skip(1).Any(s => Words(s).Any(w => !Notes.Contains(w) && !PrepWords.Contains(w) && !Joiners.Contains(w)));
        }

        private static bool IsPrep(string word) => Notes.Contains(word) || PrepWords.Contains(word);

        internal static List<string> Words(string text) =>
            Word.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
    }
}
