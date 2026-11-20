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

        private static readonly HashSet<string> Joiners = new(StringComparer.Ordinal) { "and", "or", "with", "&", "+", "n'" };

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
            "lengthwise", "crosswise", "torn", "broken", "thawed", "chilled", "warmed", "heated", "kept", "reserved", "dusting",
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

        internal static bool IsCompound(string wording)
        {
            if (wording.IndexOfAny(new[] { '+', '&', ';' }) >= 0) return true;
            var segments = wording.ToLowerInvariant().Split(',');
            if (segments.Any(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(Joiners.Contains))) return true;
            return segments.Skip(1).Any(s => Words(s).Any(w => !Notes.Contains(w)));
        }

        internal static List<string> Words(string text) =>
            Word.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
    }
}
