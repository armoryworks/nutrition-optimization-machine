using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Deterministic cleanup of scraped ingredient names before they become (or
    /// are matched against) catalog rows: strips leading quantities and units,
    /// parenthetical prep notes and stray punctuation. The recipe's own
    /// RecipeIngredient.RawLine keeps the original text, so nothing is lost.
    /// Pure string rules — no model, no network (see audit N-68/N-79).
    /// </summary>
    public static partial class IngredientNameNormalizer
    {
        private const string Fractions = "¼½¾⅓⅔⅛⅜⅝⅞";

        // "1", "1.5", "1,5", "1/2", "1 1/2", "½", "1½", and ranges "1-2" / "1 to 2".
        private static readonly string Number =
            $@"(?:(?!0\d)\d+(?:[.,]\d+)?(?:\s*/\s*\d+)?(?:(?:\s+|-)\d+\s*/\s*\d+)?[{Fractions}]?|[{Fractions}])";

        private const string FractionWord =
            @"(?:one|two|three|five|seven)[- ](?:half|halves|thirds?|fourths?|quarters?|eighths?)";

        private static readonly Regex LeadingQuantity = new(
            $@"^\s*(?:(?:and|or|to|about|nearly|circa)\s+)?(?:{Number}(?:\s*(?:-|–|—|to|or)\s*{Number})?(?:-(?=(?:lbs?|ozs?|pounds?|ounces?|pints?|pts?|quarts?|qts?|cups?)\b)|(?=[\s.])[\s.]+)|(?:a\s+(?:couple|pair)(?:\s+of)?|{FractionWord}|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|half(?![\s-]+(?:and|&|n|'n'?)[\s-]+half\b)|halves|quarters?|thirds?|(?:couple|pair)(?=\s+of\b)|mezz[oa])(?=[\s.])[\s.]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const string Qualifier =
            @"(?:(?:small|medium|large|extra[- ]large|heaping|heaped|scant|level|generous|rounded|rounding|good|great|liberal|even|full)\s+){0,2}";

        private const string SpoonOrCup =
            @"table[- ]?spoons?(?:[- ]?full?s?)?|tablespoonsful|tea[- ]?spoons?(?:[- ]?full?s?)?|teaspoonsful|dessert[- ]?spoons?(?:[- ]?full?s?)?|" +
            @"salt[- ]?spoons?(?:[- ]?full?s?)?|spoonfull?s?|spoonsful|cupfull?s?|cupsful|tea[- ]?cups?(?:[- ]?full?s?)?|teacupsful|coffee[- ]?cups?(?:[- ]?full?s?)?|" +
            @"wine[- ]?glass(?:es)?(?:[- ]?full?s?)?|tumblers?(?:[- ]?full?s?)?|[gj]ills?|tablespns?|teaspns?|dessertspns?";

        private static readonly Regex LeadingUnit = new(
            @"^\s*" + Qualifier +
            @"(?:(?:" + SpoonOrCup + @"|pints?|pts?\.?|quarts?|qts?\.?|gallons?|gals?\.?|cups?|(?:(?:No\.?|#)\s*\d+(?:\s*½|\s*[-\s]\s*1/2)?\s+(?:size\s+)?)?cans?|jars?|packages?|pkgs?|packets?|boxes?|bags?|bunch(?:es)?|" +
            @"slices?|pounds?|lbs?\.?|ounces?|ozs?\.?|fl\.?\s*oz\.?|tablespoons?|tbsps?\.?|tbps?\.?|tblsps?\.?|tbls?\.?|teaspoons?|tsps?\.?|ponies|pony|" +
            @"cloves?(?!\s*(?:,|and\b|&))|sticks?|pieces?|pinch(?:es)?|dash(?:es)?|handfull?s?|sprigs?|heads?|stalks?|ribs?|ears?|" +
            @"fillets?|filets?|strips?|cubes?|knobs?|pats?|liters?|litres?|l\b|ml\b|milliliters?|grams?|g\b|kgs?|kilograms?|" +
            @"grammi|grammo|gr\b(?=\.?\s*(?:\d|di\b))|chilogrammi|chilogrammo|litri|litro|decilitri|decilitro|dl\b)" +
            @"\b\.?\s*(?:each\s+)?(?:(?:of|di)\s+)?" +
            @"|(?:glass(?:es|full?s?)?|drops?|blades?|grains?|lumps?|bits?)\s+of\s+" +
            @"|(?:c|t|tb|tbs)\.?[\s-]+(?=\p{L}{3}))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LeadingFiller = new(
            @"^\s*(?:[*~•·]+\s*|(?:of|di)\s+|each\s+(?:of\s+)?|(?:an?\s+)?(?:very\s+)?(?:little(?!\s+(?:gem|neck))|few|some|enough|plenty\s+of)\s+(?:of\s+)?(?:the\s+)?" +
            @"|(?:(?:entirely|totally|completely|strictly)\s+)?optional\s*[:\-–,]\s*|optional\s+(?=[\d" + Fractions + @"])" +
            @"|(?:just|a\s+bit|slightly)\s+(?:under|over|less\s+than|more\s+than)\s+(?=[\d" + Fractions + @"])" +
            @"|un\s+po['’]?\s+(?:di\s+)?|(?:an?\s+)?(?:small\s+|little\s+)?quantity\s+of\s+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingNote = new(
            @"(?:\s*[,;—-])?\s*(?:or\s+(?:more\s+)?)?(?:to\s+(?:your\s+)?taste|as\s+needed|if\s+liked|if\s+desired|optional)\.?\s*$|\s*\*+\s*$|\s*,?\s+q\.\s?b\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingQuantity = new(
            $@"\s*,?\s+(?:circa\s+)?(?:{Number}(?:\s*(?:-|–|to)\s*{Number})?|{FractionWord}|one|two|three|four|five|six|half)\s*-?\s*(?:" + SpoonOrCup +
            @"|pints?|pts?|quarts?|qts?|cups?|pounds?|lbs?|ounces?|ozs?|tablespoons?|tbsps?|teaspoons?|tsps?|grams?|g|kgs?|ml|grammi|grammo|gr|chilogrammi|litri|litro|decilitri|dl)\.?\s*$" +
            $@"|\s*,?\s+(?:grammi|gr|g|kg|chilogrammi|litri|litro|dl)\.?\s*(?:circa\s+)?{Number}\.?\s*$|\s*,?\s+N\.\s*\d+\.?\s*$|\s*,\s*{Number}\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingSize = new(
            @"(?:\s*,\s*|\s+)(?:(?:of|in)\s+)?(?:an?\s+)?(?:(?:very|fairly|rather|quite)\s+)?(?:small|medium|large|middling|moderate|good|fair|big|nice|even)[- ]?siz(?:e[ds]?|ed)\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PartOf = new(
            $@"^\s*(?:the\s+)?(?:(?:grated|strained|fresh|beaten|well[- ]beaten|yellow|thin)\s+)*(?<part>juice|rind|zest|peel|yolks?|yelks?|whites?)\s+(?:and\s+(?:grated\s+)?(?:rind|juice|zest|peel)\s+)?(?:of|from)\s+" +
            $@"(?:(?:{Number}|{FractionWord}|half\s+an?|an?|the|one|two|three|four|five|six|seven|eight|nine|ten|twelve)\s+)?(?:(?:large|small|medium|fresh|raw|hard[- ]boiled|ripe|juicy)\s+)*(?<food>lemon|lime|orange|grapefruit|egg)s?\b[\s.,]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SmallFraction = new(
            @"([⁰¹²³⁴⁵⁶⁷⁸⁹]+)⁄([₀₁₂₃₄₅₆₇₈₉]+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Parenthetical = new(
            @"\s*\([^()]*\)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Whitespace = new(
            @"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name?.Trim() ?? string.Empty;

            var s = PlainFractions(WebUtility.HtmlDecode(name)).Trim();

            for (var pass = 0; pass < 3; pass++)
            {
                s = Parenthetical.Replace(s, string.Empty);
            }

            string before;
            do
            {
                before = s;
                s = LeadingFiller.Replace(s, string.Empty);
                s = LeadingQuantity.Replace(s, string.Empty);
                s = LeadingUnit.Replace(s, string.Empty);
                s = TrailingNote.Replace(s, string.Empty);
                s = TrailingQuantity.Replace(s, string.Empty);
                s = TrailingSize.Replace(s, string.Empty);
            } while (s != before && s.Length > 0);

            var part = PartOf.Match(s);
            if (part.Success) s = PartName(part.Groups["food"].Value, part.Groups["part"].Value);

            s = Whitespace.Replace(s, " ").Trim().Trim(',', ';', ':', '-', '–', '.', ' ');

            // Over-stripping ("2 cups" alone, "half") must not mint an empty or
            // one-letter name — keep the original in that case.
            return s.Length >= 2 ? s : name.Trim();
        }

        /// <summary>"juice of one lemon" names the food "lemon juice"; "yolks of two eggs" names "egg yolks".</summary>
        private static string PartName(string food, string part)
        {
            var lowered = part.ToLowerInvariant();
            if (lowered.StartsWith("yelk", StringComparison.Ordinal)) lowered = "yolk";
            return food.Equals("egg", StringComparison.OrdinalIgnoreCase)
                ? $"egg {lowered.TrimEnd('s')}s"
                : $"{food.ToLowerInvariant()} {lowered}";
        }

        /// <summary>Rewrites typeset fractions ("¹⁄₂") as plain ones ("1/2") so the quantity rules see them.</summary>
        public static string PlainFractions(string text) => text.IndexOf('⁄') < 0 ? text : SmallFraction.Replace(text, m =>
            string.Concat(m.Groups[1].Value.Select(c => "⁰¹²³⁴⁵⁶⁷⁸⁹".IndexOf(c))) + "/" + string.Concat(m.Groups[2].Value.Select(c => "₀₁₂₃₄₅₆₇₈₉".IndexOf(c))));

        private static readonly Regex LeadingDescriptor = new(
            @"^\s*(?:(?:finely|coarsely|roughly|thinly|freshly|well|very|lightly|nicely)[- ]\s*)?" +
            @"(?:chopped|minced|sliced|diced|grated|shredded|melted|softened|beaten|sifted|boiling|cold|warm|hot|lukewarm|tepid|large|small|medium|medium[- ]sized|good[- ]sized|" +
            @"good|nice|best|peeled|pared|cored|stoned|pitted|seeded|trimmed|washed|picked|cleaned|quartered|halved|cubed|strained|firm|tender|young|fine)\s+(?=\p{L})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingDescriptor = new(
            @"(?:\s*,\s*|\s+)(?:(?:and|finely|coarsely|roughly|thinly|freshly|well|lightly|very)\s+)*" +
            @"(?:chopped|minced|sliced|diced|grated|shredded|melted|softened|beaten|sifted|peeled|pared|cored|cubed|halved|quartered|divided|drained|rinsed|trimmed|juiced|zested|" +
            @"(?:at\s+)?room\s+temperature)(?:\s+(?:fine|finely|small|up|thin|thinly))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// The name with its preparation words taken off as well ("chopped onion", "onion, sliced
        /// thin" → "onion"), for finding the food a wording means. Words that change what the food
        /// is (dried, ground, sweet, powdered, a colour) stay. Never stored as a name.
        /// </summary>
        public static string Bare(string name)
        {
            var s = Normalize(name);
            string before;
            do
            {
                before = s;
                var stripped = TrailingDescriptor.Replace(LeadingDescriptor.Replace(s, string.Empty), string.Empty).Trim().Trim(',', ';', ' ');
                if (stripped.Length >= 3) s = stripped;
            } while (s != before);
            return s;
        }

        private static readonly System.Collections.Generic.HashSet<string> NumberChangesMeaning = new(StringComparer.OrdinalIgnoreCase)
        {
            "pepper", "peppers", "green", "greens", "sweet", "sweets", "bitter", "bitters", "clove", "cloves",
            "ice", "ices", "cream", "creams", "spirit", "spirits", "stock", "stocks",
        };

        /// <summary>
        /// The names a wording may be found under, most exact first: the normalised name, its
        /// singular and plural, then the same for the bare name. A word whose plural is another food
        /// ("pepper" the spice, "peppers" the vegetable) is not folded.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<string> LookupKeys(string name)
        {
            var keys = new System.Collections.Generic.List<string>();
            AddWithNumber(keys, Normalize(name));
            AddWithNumber(keys, Bare(name));
            return keys;
        }

        /// <summary>
        /// The names a wording may be found under with no word taken off: the normalised name, its
        /// singular and its plural.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<string> ExactKeys(string name)
        {
            var keys = new System.Collections.Generic.List<string>();
            AddWithNumber(keys, Normalize(name));
            return keys;
        }

        private static void AddWithNumber(System.Collections.Generic.List<string> keys, string key)
        {
            void Add(string candidate)
            {
                if (candidate.Length >= 2 && !keys.Contains(candidate, StringComparer.OrdinalIgnoreCase)) keys.Add(candidate);
            }
            Add(key);
            if (NumberChangesMeaning.Contains(key)) return;
            if (key.EndsWith("ies", StringComparison.OrdinalIgnoreCase)) Add(key[..^3] + "y");
            if (key.EndsWith("es", StringComparison.OrdinalIgnoreCase)) Add(key[..^2]);
            if (key.EndsWith("s", StringComparison.OrdinalIgnoreCase)) Add(key[..^1]);
            else { Add(key + "s"); Add(key + "es"); }
        }

        /// <summary>
        /// The key a wording is judged and looked up under in the verified wording table: the
        /// normalised name, lower-cased, with no word folded or taken off.
        /// </summary>
        public static string WordingKey(string name) => Normalize(name).ToLowerInvariant();

        /// <summary>
        /// The key two catalog food names share when they name the same food: the wording key with
        /// a bracketed cut or form ("top loin (roasts)" against "(chops)") kept, since in a catalog
        /// name it is part of what the food is, not a cook's note.
        /// </summary>
        public static string CatalogKey(string name) => WordingKey((name ?? string.Empty).Replace('(', '[').Replace(')', ']'));

        private static readonly Regex HeadingLine = new(
            @"^\s*for\s+(?:the\s+)?\p{L}|:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OldFiller = new(
            @"^\s*(?:(?:the|a|an|some|nice|good|best|fine|finest|very|little|small\s+quantity\s+of|remains\s+of|rest\s+of|(?:a\s+)?(?:quarter|half|third)\s+of\s+(?:a|an|the)\s+(?:pound|pint|quart|cup|ounce)\s+of|of\s+(?:a|an|the)\s+(?:pound|pint|quart|cup|ounce)\s+of|of)\s+)+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingPurpose = new(
            @"(?:\s*,\s*|\s+)(?:(?:plus|and)\s+(?:more|extra)|(?:for|to)\s+(?:garnish(?:ing)?|serv(?:e|ing)|frying|dusting|greasing|brushing|decorat(?:e|ing|ion)|sprinkling|the\s+\p{L}+)|divided|separated|or\s+more|or\s+to\s+taste)\b.*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// The harder key the deterministic wording tiers decide on: the normalised name with its
        /// preparation words, its notes of purpose ("for garnish", ", divided") and the old
        /// cookbooks' filler ("the", "nice", "good", "best", "fine", "remains of", "of a pound of")
        /// taken off, lower-cased. Null for a heading line ("For the frosting:"), which names no
        /// food. Never stored: the verified wording table keeps <see cref="WordingKey"/>.
        /// </summary>
        public static string? TierKey(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var normalized = Normalize(name);
            if (HeadingLine.IsMatch(normalized) || HeadingLine.IsMatch(name.Trim())) return null;

            var s = normalized;
            string before;
            do
            {
                before = s;
                var stripped = TrailingPurpose.Replace(OldFiller.Replace(s, string.Empty), string.Empty).Trim().Trim(',', ';', ':', '-', '.', ' ');
                if (stripped.Length >= 2) s = stripped;
                var bare = Bare(s);
                if (bare.Length >= 2) s = bare;
            } while (s != before);

            s = Whitespace.Replace(s, " ").Trim().ToLowerInvariant();
            return s.Length >= 2 ? s : null;
        }

        /// <summary>True when normalization would change this catalog name.</summary>
        public static bool IsResidue(string name) =>
            !string.Equals(Normalize(name), name?.Trim(), StringComparison.Ordinal);

        private static readonly Regex AmountWord = new(
            @"\b(?:circa|ca\.?|N\.?|nr\.?|no\.?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// True when a normalized name holds no food: only numbers, punctuation and amount words such as
        /// "circa" ("100", "circa 100" left by "Grammi circa 100"). No food is ever named so.
        /// </summary>
        public static bool NamesNoFood(string? name) =>
            !AmountWord.Replace(name ?? string.Empty, string.Empty).Any(char.IsLetter);

        private static readonly Regex MeasureOnly = new(
            $@"^\s*(?:(?:about|circa|ca\.?)\s+)?(?:{Number}\s*)?(?:(?:circa|ca\.?)\s+)?" +
            @"(?:cups?|c|pints?|pts?|quarts?|qts?|gallons?|gals?|pounds?|lbs?|ounces?|ozs?|fl\.?\s*oz|tablespoons?|tbsps?|tbs|tbl|tbsp|teaspoons?|tsps?|t|" +
            @"liters?|litres?|l|ml|milliliters?|grams?|g|kgs?|kilograms?|grammi|grammo|gr|chilogrammi|chilogrammo|litri|litro|decilitri|decilitro|dl|pinch(?:es)?|dash(?:es)?)" +
            $@"\b\.?\s*(?:(?:circa|ca\.?)\s+)?(?:{Number})?\s*\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// True when a line's text states only an amount and names no food: a number alone, or a number
        /// and a measure ("Grammi circa 100", "Grammi 80", "2 cups", "1 tbsp."). A count word that can be
        /// a food itself ("6 cloves", "2 heads") is not read so.
        /// </summary>
        public static bool NamesOnlyAnAmount(string? text) =>
            NamesNoFood(Normalize(text ?? string.Empty)) || MeasureOnly.IsMatch(text ?? string.Empty);
    }
}
