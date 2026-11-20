using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The rule checks the reprocess lane runs on every line a rebuild changes, written apart from
    /// the line builder so they do not share its mistakes (reprocess.md section 3). A1: a stored
    /// amount stands in the line's text, or for a method fill in a method sentence that names the
    /// food, as a number read here (digits, fractions, number words, "a couple") times one of the
    /// old measures' factors. A2: a line the rebuild moved to another food names that food. A3: a
    /// count in pieces is not of a liquid or of a line that states a measure, and a whole bird is
    /// not measured by volume. A4 compares a recipe's failures before and after. No model and no
    /// database.
    /// </summary>
    public static class ReprocessRuleAudit
    {
        public const string Version = "reprocess-audit/1";

        public const string Grounded = "A1";
        public const string Named = "A2";
        public const string UnitSane = "A3";
        public const string NoWorse = "A4";
        public const string Idempotent = "A5";

        /// <summary>One line row as the checks read it.</summary>
        public sealed record Line(
            string Raw, string? Food, decimal Quantity, string? Unit, string? Kind,
            bool FilledFromMethod = false, bool FoodChanged = false, decimal? GramsPerItem = null);

        private static readonly (Regex Word, decimal Factor)[] Factors =
        {
            (new Regex(@"\bgallons?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 16m),
            (new Regex(@"\bquarts?\b|\bqts?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 4m),
            (new Regex(@"\bpints?\b|\bpts?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 2m),
            (new Regex(@"\b(?:gills?|jills?|tea-?cups?\w*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 0.5m),
            (new Regex(@"\bwine-?glass\w*\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 0.25m),
            (new Regex(@"\bdessert-?sp\w*\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 2m),
            (new Regex(@"\bsalt-?spoon\w*\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 0.25m),
            (new Regex(@"\bdozen\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 12m),
            (new Regex(@"\bpounds?\b|\blbs?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 16m),
            (new Regex(@"\bponies\b|\bpony\b|\bfl\.?\s*oz\b|\bfluid ounces?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 29.5735m),
            (new Regex(@"\bdecilit\w*\b|\bdl\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), 100m),
        };

        private static readonly Dictionary<string, decimal> Words = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
            ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fifteen"] = 15,
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["hundred"] = 100,
            ["half"] = 0.5m, ["quarter"] = 0.25m, ["third"] = 1m / 3m, ["couple"] = 2, ["pair"] = 2, ["dozen"] = 12,
            ["un"] = 1, ["uno"] = 1, ["una"] = 1, ["due"] = 2, ["tre"] = 3, ["quattro"] = 4, ["mezzo"] = 0.5m,
        };

        private static readonly Dictionary<char, decimal> Vulgar = new()
        {
            ['¼'] = 0.25m, ['½'] = 0.5m, ['¾'] = 0.75m, ['⅓'] = 1m / 3m, ['⅔'] = 2m / 3m, ['⅛'] = 0.125m,
            ['⅜'] = 0.375m, ['⅝'] = 0.625m, ['⅞'] = 0.875m, ['⅕'] = 0.2m, ['⅖'] = 0.4m, ['⅗'] = 0.6m, ['⅘'] = 0.8m, ['⅙'] = 1m / 6m, ['⅚'] = 5m / 6m,
        };

        private static readonly Regex Numeric = new(
            @"(?<whole>\d+(?:[.,]\d+)?)?\s*(?:-\s*)?(?:(?<num>\d+)\s*[/⁄]\s*(?<den>\d+)|(?<vulgar>[¼½¾⅓⅔⅛⅜⅝⅞⅕⅖⅗⅘⅙⅚]))|(?<plain>\d+(?:[.,]\d+)?)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Measure = new(
            @"\b(?:cups?|cupfuls?|tea-?cups?|tumblers?|glass(?:es|fuls?)?|spoons?|spoonfuls?|tablesp\w*|teasp\w*|tbsp?s?|tsps?|tbp|dessertsp\w*|pints?|quarts?|qts?|gallons?|gills?|jills?|pounds?|lbs?|ounces?|oz|grams?|grammi|gr|kilo\w*|kg|litres?|liters?|litro|ml|pinch(?:es)?|dash(?:es)?)\b\.?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] Volume = { "Cup", "Tablespoon", "Teaspoon", "Milliliter", "Liter", "Pint", "Quart", "Gallon", "Fluid Ounce" };

        private static readonly HashSet<string> Liquids = new(StringComparer.OrdinalIgnoreCase)
        {
            "water", "milk", "cream", "broth", "stock", "wine", "vinegar", "oil", "juice", "syrup", "brandy", "rum", "sherry", "beer",
            "cider", "buttermilk", "molasses", "honey", "liquor", "whisky", "whiskey", "vodka", "gin", "acqua", "latte", "brodo", "olio", "vino",
        };

        private static readonly HashSet<string> Birds = new(StringComparer.OrdinalIgnoreCase)
        {
            "chicken", "chickens", "duck", "ducks", "turkey", "goose", "fowl", "fowls", "pigeon", "pigeons", "partridge", "partridges",
            "quail", "quails", "pheasant", "capon", "hen", "pollastra", "pollo",
        };

        private static readonly HashSet<string> BirdParts = new(StringComparer.OrdinalIgnoreCase)
        {
            "broth", "stock", "fat", "liver", "livers", "meat", "breast", "breasts", "gravy", "soup", "giblets", "drippings", "bouillon", "base",
        };

        /// <summary>Every number the text states, in the order read: digits, fractions, mixed numbers and number words.</summary>
        public static IReadOnlyList<decimal> Numbers(string? text)
        {
            var found = new List<decimal>();
            if (string.IsNullOrWhiteSpace(text)) return found;
            foreach (Match m in Numeric.Matches(text))
            {
                if (m.Groups["plain"].Success)
                {
                    if (Read(m.Groups["plain"].Value) is { } plain) found.Add(plain);
                    continue;
                }
                var whole = m.Groups["whole"].Success ? Read(m.Groups["whole"].Value) ?? 0 : 0;
                decimal part = 0;
                if (m.Groups["vulgar"].Success) part = Vulgar[m.Groups["vulgar"].Value[0]];
                else if (decimal.TryParse(m.Groups["den"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var den) && den > 0)
                    part = decimal.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture) / den;
                found.Add(whole + part);
                if (whole > 0) found.Add(whole);
            }
            foreach (Match w in Word.Matches(text))
            {
                if (Words.TryGetValue(w.Value, out var value)) found.Add(value);
            }
            return found;
        }

        private static decimal? Read(string text) =>
            decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        /// <summary>
        /// A1: whether the amount stands in the text: equal, within a hundredth, to a stated number,
        /// or to one times the factor of an old measure the text names (a quart is 4 cups, a gill half
        /// a cup, a pound 16 ounces), or to a number between two stated ones (a range), or to a stated
        /// number times or plus another ("2 cans of 15 oz", "1 pound 4 ounces").
        /// </summary>
        public static bool GroundedIn(decimal quantity, IEnumerable<string?> texts)
        {
            if (quantity <= 0) return true;
            var given = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
            var numbers = given.SelectMany(Numbers).Distinct().ToList();
            var factors = Factors.Where(f => given.Any(t => f.Word.IsMatch(t))).Select(f => f.Factor).Prepend(1m).ToList();
            if (numbers.Any(n => factors.Any(f => Near(n * f, quantity)))) return true;
            for (var i = 0; i < numbers.Count; i++)
            {
                for (var j = i + 1; j < numbers.Count; j++)
                {
                    var (low, high) = numbers[i] < numbers[j] ? (numbers[i], numbers[j]) : (numbers[j], numbers[i]);
                    if (quantity >= low && quantity <= high) return true;
                    if (Near(numbers[i] * numbers[j], quantity) || Near(numbers[i] + numbers[j], quantity)) return true;
                }
            }
            return false;
        }

        private static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.01m;

        private static readonly Regex LeadNumber = new(
            @"^\s*(?:about\s+|~\s*)?(?:(?<digit>[\d¼½¾⅓⅔⅛⅜⅝⅞])|(?<word>(?:one|two|three|four|five|six|eight|ten|twelve|half\s+an?|an?)\s+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether the line states an amount at its start: it begins with a number ("2 eggs", "½ cup
        /// sugar"), or with a number word followed by a measure ("two cups of milk", "a pint of
        /// cream"). Such a line stored with no amount has lost it.
        /// </summary>
        public static bool StatesAmount(string? raw)
        {
            var lead = (raw ?? string.Empty).Split(FoldedLine.Separator, 2)[0];
            var m = LeadNumber.Match(lead);
            if (!m.Success) return false;
            if (m.Groups["digit"].Success) return true;
            var rest = lead[(m.Index + m.Length)..].TrimStart();
            return Measure.Match(rest) is { Success: true, Index: 0 };
        }

        /// <summary>The method sentences a fill may come from: those that name the line's food.</summary>
        public static IEnumerable<string> MethodSentences(IEnumerable<string> steps, string? food, string raw) =>
            steps.SelectMany(s => Regex.Split(s ?? string.Empty, @"(?<=[.;!?])\s+"))
                .Where(s => FoodNameMatcher.NamesFood(s, food) && (food != null || FoodNameMatcher.NamesFood(s, raw)));

        /// <summary>The checks of A1 to A3 that fail on one line, by code; empty when all pass.</summary>
        public static IReadOnlyList<string> Failures(Line line, IEnumerable<string>? method = null)
        {
            var failures = new List<string>();
            if (line.Kind != null && line.Kind != "ingredient" && line.Quantity == 0) return failures;

            var texts = new List<string?> { line.Raw };
            if (line.FilledFromMethod && method != null) texts.AddRange(MethodSentences(method, line.Food, line.Raw));
            if (!GroundedIn(line.Quantity, texts) || (line.Quantity == 0 && line.Kind is null or "ingredient" && StatesAmount(line.Raw))) failures.Add(Grounded);

            if (line.FoodChanged && line.Kind is null or "ingredient" && !FoodNameMatcher.NamesFood(line.Raw, line.Food)) failures.Add(Named);

            if (line.Quantity > 0 && !UnitSaneFor(line)) failures.Add(UnitSane);
            return failures;
        }

        private static bool UnitSaneFor(Line line)
        {
            var foodWords = Word.Matches(line.Food ?? string.Empty).Select(m => m.Value).ToList();
            if (line.Unit == IngredientLineParser.Piece)
            {
                if (foodWords.Any(Liquids.Contains) && !(line.GramsPerItem > 0)) return false;
                var lead = line.Raw.Split(FoldedLine.Separator, 2)[0];
                if (Measure.IsMatch(lead) && !IngredientLineParser.ReadsPackage(lead) && !(line.GramsPerItem > 0)) return false;
            }
            if (line.Unit != null && Volume.Contains(line.Unit, StringComparer.OrdinalIgnoreCase)
                && foodWords.Any(Birds.Contains) && !foodWords.Any(BirdParts.Contains))
            {
                return false;
            }
            return true;
        }
    }
}
