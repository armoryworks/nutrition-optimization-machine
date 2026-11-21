using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>The title a recipe keeps, and why it changed (null when the title is only the dish name).</summary>
    public sealed record RecipeTitleReading(string Title, string? Reason)
    {
        public bool Changed => Reason != null;
    }

    /// <summary>
    /// The title rule for recipes from a facts-only source: a title that is only the dish name is kept;
    /// a distinctive or branded title is reduced to the dish name, by rule and no model. In order: a tail
    /// after " | ", " - ", " – " or " — " goes, as do bracketed asides and a trailing "recipe"; a boast
    /// clause ("you'll ever make", "of your life", "to die for") goes; a leading possessive goes when it
    /// names family ("Grandma's", "Aunt Bee's") or the source itself (a name in the source's author or
    /// site, "Sally's" for Sally's Baking Addiction), so "Shepherd's Pie" and "General Tso's Chicken"
    /// keep theirs; "copycat" goes; then praise words (best, ultimate, perfect, amazing, famous, easy ...)
    /// and the articles and pronouns left in front of the dish go. If nothing of a dish name would be
    /// left, the title is kept as it was.
    /// </summary>
    public static class RecipeTitleRule
    {
        public const string Version = "recipe-titles/1";

        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        private static readonly Regex Tail = new(@"\s+(?:\||-|–|—)\s+.*$", Options);
        private static readonly Regex Brackets = new(@"\s*[\(\[][^\)\]]*[\)\]]", Options);
        private static readonly Regex RecipeWord = new(@"\s+recipes?\s*$", Options);
        private static readonly Regex Boast = new(
            @"\s+(?:(?:that\s+)?you(?:'|’)?ll\s+ever\s+\w+|you\s+will\s+ever\s+\w+|(?:of|in)\s+your\s+life|ever\s+made|to\s+die\s+for|in\s+the\s+world|on\s+the\s+planet|of\s+all\s+time|you\s+need\s+to\s+(?:make|try)(?:\s+\w+)?|everyone\s+(?:loves|will\s+love))\b.*$",
            Options);
        private static readonly Regex Possessive = new(@"^(?<who>(?:[\w&.]+\s+){0,2}[\w&.]+)(?:'|’)s\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Word = new(@"\p{L}[\p{L}\-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex AuthorName = new(@"\brecipe\s+by\s+(?<who>[^,;]+)", Options);
        private static readonly Regex SiteOwner = new(@"(?<who>[\p{L}\-]+)(?:'|’)s\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Family = new(StringComparer.OrdinalIgnoreCase)
        {
            "grandma", "grandmas", "granny", "gran", "nana", "nanna", "nonna", "mom", "moms", "mommy", "mum", "mums", "mama", "momma",
            "mother", "dad", "dads", "daddy", "papa", "pop", "grandpa", "grandad", "granddad", "father", "aunt", "auntie", "aunty",
            "uncle", "my", "our",
        };
        private static readonly Regex Copycat = new(@"\bcopy\s?cat\b\s*", Options);

        private static readonly HashSet<string> Praise = new(StringComparer.OrdinalIgnoreCase)
        {
            "best", "bestest", "ultimate", "perfect", "perfectly", "amazing", "amazingly", "incredible", "incredibly", "awesome",
            "famous", "legendary", "epic", "magic", "magical", "heavenly", "addictive", "irresistible", "foolproof", "fool-proof",
            "life-changing", "mind-blowing", "insanely", "seriously", "really", "very", "super", "absolutely", "absolute",
            "favorite", "favourite", "world's", "world’s", "award-winning", "prize-winning", "killer", "unbelievable",
            "crave-worthy", "craveable", "delicious", "deliciously", "yummy", "tasty", "fabulous", "wonderful", "glorious",
            "dreamy", "decadent", "sinful", "easy", "easiest", "ever", "greatest",
        };

        private static readonly HashSet<string> Lead = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "my", "our", "your", "this", "these", "most", "so", "and", "&",
        };

        /// <summary>
        /// The names that brand a title as the source's own: every name in the author's byline ("Recipe by
        /// Sally McKenney") and the owner in a possessive site name ("Sally's Baking Addiction").
        /// </summary>
        public static IReadOnlyCollection<string> BrandNames(params string?[] credits)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var credit in credits.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                foreach (Match author in AuthorName.Matches(credit!))
                {
                    foreach (Match word in Word.Matches(author.Groups["who"].Value))
                    {
                        if (word.Value.Length >= 3 && char.IsUpper(word.Value[0])) names.Add(word.Value);
                    }
                }

                foreach (Match owner in SiteOwner.Matches(credit!))
                {
                    if (owner.Groups["who"].Value.Length >= 3) names.Add(owner.Groups["who"].Value);
                }
            }

            return names;
        }

        public static RecipeTitleReading Reduce(string? title, IReadOnlyCollection<string>? brands = null)
        {
            var original = Regex.Replace(title ?? string.Empty, @"\s+", " ").Trim();
            if (original.Length == 0) return new RecipeTitleReading(original, null);

            var reasons = new List<string>();
            var text = original;
            text = Step(text, Tail, string.Empty, "tail", reasons);
            text = Step(text, Brackets, string.Empty, "aside", reasons);
            text = Step(text, Boast, string.Empty, "boast", reasons);
            text = Step(text, RecipeWord, string.Empty, "recipe word", reasons);
            if (Possessive.Match(text) is { Success: true } possessive
                && Word.Matches(possessive.Groups["who"].Value).Any(w => Family.Contains(w.Value) || (brands?.Contains(w.Value) ?? false)))
            {
                text = Step(text, new Regex("^" + Regex.Escape(possessive.Value)), string.Empty, "name", reasons);
            }

            text = Step(text, Copycat, string.Empty, "copycat", reasons);

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var dropped = false;
            while (words.Count > 1 && (Praise.Contains(Bare(words[0])) || Lead.Contains(Bare(words[0]))))
            {
                if (Praise.Contains(Bare(words[0]))) dropped = true;
                words.RemoveAt(0);
            }

            if (dropped) reasons.Add("praise");
            text = string.Join(' ', words).Trim(' ', ',', ';', ':', '!', '.', '-');
            if (reasons.Count == 0) return new RecipeTitleReading(original, null);
            if (text.Length == 0 || words.All(w => Praise.Contains(Bare(w)) || Lead.Contains(Bare(w)))) return new RecipeTitleReading(original, null);
            text = char.ToUpperInvariant(text[0]) + text[1..];
            return string.Equals(text, original, StringComparison.Ordinal)
                ? new RecipeTitleReading(original, null)
                : new RecipeTitleReading(text, string.Join(", ", reasons));
        }

        private static string Bare(string word) => word.Trim(',', '!', '.', ':', ';');

        private static string Step(string text, Regex pattern, string replacement, string reason, List<string> reasons)
        {
            var next = pattern.Replace(text, replacement).Trim();
            if (next.Length == 0 || next == text) return text;
            reasons.Add(reason);
            return next;
        }
    }

    /// <summary>
    /// Third-party credit lines in a recipe's source text: "Adapted from ...", "Recipe from ...", "Recipe
    /// courtesy of ...", "Courtesy of ...", "Reprinted (with permission) from ...", "Inspired by ...",
    /// "Excerpted from ...". A line that credits the author's own work or the page itself ("adapted from my
    /// cupcakes", "recipe from this website", "recipe from now on") is not a credit. Each credit is kept as
    /// its cue and the name it gives, up to the end of the clause.
    /// </summary>
    public static class CreditLines
    {
        private static readonly Regex Credit = new(
            @"\b(?<cue>(?:recipe\s+)?adapted\s+(?:slightly\s+)?from|recipe\s+(?:courtesy\s+of|from)|courtesy\s+of|reprinted\s+(?:with\s+permission\s+)?from|excerpted\s+from|inspired\s+by)\s+(?<who>[^.;!?\n\(\)]{2,90})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> Own = new(StringComparer.OrdinalIgnoreCase)
        {
            "my", "our", "this", "these", "that", "those", "the", "a", "an", "now", "when", "here", "him", "her", "his", "their",
            "your", "yours", "me", "us", "it", "there", "scratch", "memory", "one", "another", "some", "several", "many", "what",
            "how", "years", "i", "we", "you", "she", "he", "they", "mine", "ours", "every", "all", "any",
        };

        public static IReadOnlyList<string> Find(IEnumerable<string?> texts)
        {
            var found = new List<string>();
            foreach (var text in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                foreach (Match match in Credit.Matches(text!))
                {
                    var who = Regex.Replace(match.Groups["who"].Value, @"\s+", " ").Trim().TrimEnd(',', ':', '-', '–', '—', ' ');
                    var first = who.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"', '\'', '“', '‘') ?? string.Empty;
                    if (first.Length == 0 || Own.Contains(first)) continue;
                    var cue = Regex.Replace(match.Groups["cue"].Value, @"\s+", " ").Trim();
                    var line = char.ToUpperInvariant(cue[0]) + cue[1..].ToLowerInvariant() + " " + who;
                    if (!found.Contains(line, StringComparer.OrdinalIgnoreCase)) found.Add(line);
                }
            }

            return found;
        }

        /// <summary>The credits as one stored line, or null when there is none.</summary>
        public static string? Join(IEnumerable<string?> texts)
        {
            var credits = Find(texts);
            if (credits.Count == 0) return null;
            var joined = string.Join("; ", credits);
            return joined.Length <= 1023 ? joined : joined[..1023];
        }
    }
}
