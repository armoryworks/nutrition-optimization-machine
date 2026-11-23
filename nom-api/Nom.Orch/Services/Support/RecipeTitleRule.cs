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
    /// clause ("you'll ever make", "I've ever made", "we have ever had", "of your life", "to die for") goes,
    /// its subject with it; a leading possessive goes when it
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
            @"\s+(?:(?:that\s+)?(?:i|we|you)(?:(?:'|’)ve|\s+have)\s+ever\s+\w+|(?:that\s+)?you(?:'|’)?ll\s+ever\s+\w+|you\s+will\s+ever\s+\w+|(?:of|in)\s+your\s+life|ever\s+made|to\s+die\s+for|in\s+the\s+world|on\s+the\s+planet|of\s+all\s+time|you\s+need\s+to\s+(?:make|try)(?:\s+\w+)?|everyone\s+(?:loves|will\s+love))\b.*$",
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
    /// courtesy of ...", "Courtesy of ...", "Reprinted (with permission) from ...", "Excerpted from ...".
    /// "Inspired by" names an influence, not the work a recipe was taken from, so it is no credit. A line
    /// that credits the author's own work or the page itself ("adapted from my cupcakes", "recipe from this
    /// website", "recipe from now on") is not a credit. Each credit is kept as its cue and the name it
    /// gives: the run of capitalised words (with "of", "the", "and", "by" and the like between them) that
    /// follows the cue, so no clause of the publisher's own prose is ever kept with it. The text is read
    /// with its HTML entities decoded, so "What&amp;#x27;s" is one word and "Love &amp;amp; Lemons" one
    /// name. Where the source's markup lost the break between two elements, the name ends at the break:
    /// before a word that starts a sentence ("This", "Other", "I've", "The" after a name word), before a
    /// word the name already holds ("by Tara O'Brady Tara writes"), and inside a word glued to the next
    /// element's first word ("James BeardUsing" keeps "James Beard"; only where the glued part is a word
    /// that starts a sentence, so "HarperCollins" and "KitchenAid" stay whole). A word repeated after "by"
    /// or "and" starts a new name ("Ottolenghi Simple by Yotam Ottolenghi" stays whole). A possessive ending ("America's
    /// Test Kitchen's") is dropped, a word the length limit cut is left out, and a name made only of
    /// places, nationalities or languages ("Recipe from South America", "Recipe from French cooks") is no
    /// credit (staging checkpoint 3, N26).
    /// </summary>
    public static class CreditLines
    {
        public const string Version = "credit-lines/2";

        private const int MaxName = 160;

        private static readonly Regex Credit = new(
            @"\b(?<cue>(?:recipe\s+)?adapted\s+(?:slightly\s+)?from|recipe\s+(?:courtesy\s+of|from)|courtesy\s+of|reprinted\s+(?:with\s+permission\s+)?from|excerpted\s+from)\s+(?<who>[^.;!?\n\(\)]{2," + MaxName + "})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> SentenceStarts = new(StringComparer.Ordinal)
        {
            "The", "This", "That", "These", "Those", "There", "Here", "It", "It's", "I", "I've", "I'm", "I'd", "I'll", "We", "We've", "We're",
            "You", "You'll", "You're", "He", "She", "They", "Other", "Others", "When", "While", "If", "After", "Before", "Using", "So", "But",
            "Also", "Then",
        };

        private static readonly HashSet<string> Places = new(StringComparer.OrdinalIgnoreCase)
        {
            "north", "south", "east", "west", "central", "latin", "middle", "far", "near", "eastern", "western", "northern", "southern",
            "america", "american", "americas", "africa", "african", "asia", "asian", "europe", "european", "australia", "australian",
            "french", "italian", "spanish", "mexican", "greek", "german", "indian", "chinese", "japanese", "korean", "thai", "vietnamese",
            "turkish", "lebanese", "moroccan", "persian", "russian", "polish", "irish", "english", "british", "scottish", "portuguese",
            "brazilian", "peruvian", "cuban", "caribbean", "mediterranean", "nordic", "scandinavian", "swedish", "danish", "dutch",
            "france", "italy", "spain", "mexico", "greece", "germany", "india", "china", "japan", "korea", "thailand", "vietnam",
            "england", "britain", "ireland", "scotland", "portugal", "brazil", "peru", "cuba",
        };

        private static readonly HashSet<string> Own = new(StringComparer.OrdinalIgnoreCase)
        {
            "my", "our", "this", "these", "that", "those", "the", "a", "an", "now", "when", "here", "him", "her", "his", "their",
            "your", "yours", "me", "us", "it", "there", "scratch", "memory", "one", "another", "some", "several", "many", "what",
            "how", "years", "i", "we", "you", "she", "he", "they", "mine", "ours", "every", "all", "any",
        };

        private static readonly HashSet<string> Joiners = new(StringComparer.Ordinal)
        {
            "of", "the", "and", "&", "by", "de", "du", "del", "della", "di", "da", "la", "le", "von", "van", "der", "den", "for", "in", "at", "on", "a", "an",
        };

        private static string Bare(string word) => word.Trim('"', '\'', '“', '”', '‘', '’', ',', ':');

        private static readonly HashSet<string> GluedStarts = new(SentenceStarts.Concat(new[]
        {
            "In", "On", "At", "For", "To", "With", "From", "A", "An", "My", "Our", "Your", "His", "Her", "Their", "Its", "Each", "Every",
            "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Makes", "Serves", "Yield", "Ingredients",
            "Directions", "Instructions", "Method", "Notes", "Note", "Print", "Get", "Find", "See", "Read", "Watch", "Try", "Add",
            "Mix", "Combine", "Preheat", "Heat", "Place", "Put", "Make", "Cook", "Bake", "Stir", "Whisk", "Serve",
        }), StringComparer.Ordinal);

        private static int? GluedAt(string word)
        {
            for (var i = 4; i < word.Length - 1; i++)
            {
                if (!char.IsLower(word[i - 1]) || !char.IsUpper(word[i]) || !char.IsLower(word[i + 1]) || !word[..i].All(char.IsLetter)) continue;
                var end = i + 1;
                while (end < word.Length && char.IsLower(word[end])) end++;
                if (GluedStarts.Contains(word[i..end])) return i;
            }
            return null;
        }

        private static string Name(string who)
        {
            var kept = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in who.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var bare = Bare(word);
                if (bare.Length == 0) break;
                var named = char.IsUpper(bare[0]) || char.IsDigit(bare[0]);
                if (!named && !(kept.Count > 0 && Joiners.Contains(bare))) break;
                if (named && kept.Count > 0 && !Joiners.Contains(Bare(kept[^1])) && (SentenceStarts.Contains(bare) || seen.Contains(bare))) break;
                if (GluedAt(bare) is { } glued)
                {
                    kept.Add(bare[..glued]);
                    break;
                }
                kept.Add(word);
                if (bare is "by" or "and" or "&") seen.Clear();
                else seen.Add(bare);
                if (word.EndsWith(',') || word.EndsWith(':')) break;
            }

            while (kept.Count > 0 && Joiners.Contains(Bare(kept[^1]))) kept.RemoveAt(kept.Count - 1);
            if (kept.Count > 0) kept[^1] = Regex.Replace(kept[^1], @"['’]s([,:]?)$", "$1");
            if (kept.Where(w => !Joiners.Contains(Bare(w))).All(w => Places.Contains(Bare(w)))) return string.Empty;
            return string.Join(' ', kept).TrimEnd(',', ':', ' ');
        }

        private static string Decode(string text)
        {
            var once = System.Net.WebUtility.HtmlDecode(text);
            return once.Contains('&') ? System.Net.WebUtility.HtmlDecode(once) : once;
        }

        public static IReadOnlyList<string> Find(IEnumerable<string?> texts)
        {
            var found = new List<string>();
            foreach (var raw in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var text = Decode(raw!);
                foreach (Match match in Credit.Matches(text))
                {
                    var who = match.Groups["who"].Value;
                    var end = match.Groups["who"].Index + who.Length;
                    if (who.Length == MaxName && end < text.Length && char.IsLetterOrDigit(text[end]))
                        who = who[..Math.Max(0, who.LastIndexOf(' '))];
                    who = Regex.Replace(who, @"\s+", " ").Trim().TrimEnd(',', ':', '-', '–', '—', ' ');
                    var first = who.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"', '\'', '“', '‘') ?? string.Empty;
                    if (first.Length == 0 || Own.Contains(first)) continue;
                    who = Name(who);
                    if (who.Length == 0) continue;
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
