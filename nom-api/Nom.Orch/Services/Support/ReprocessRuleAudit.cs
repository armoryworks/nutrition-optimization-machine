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
    /// food or the line's wording, as a number read here (digits, fractions, number words, "a couple"; "a" or "an" only
    /// before a measure, a can or the food, a size word such as "scant" or "large" between) times one of the old measures' factors that text names, or as
    /// pounds and ounces it states added up in ounces. A2: a
    /// line the rebuild moved to another food names that food. A3: a count in pieces is not of a
    /// liquid or of a line that states a measure, a whole bird is not measured by volume, and a volume
    /// of a seasoning or leavener stays within a quart and of a dry staple within a gallon ("1 gallon
    /// of salt" fails). A4 compares a recipe's failures before and after. A6: the kcal per serving lands in 30 to 2,500
    /// or moves no further from that band. An order restore keeps the source's order (O1). No model
    /// and no database.
    /// </summary>
    public static class ReprocessRuleAudit
    {
        public const string Version = "reprocess-audit/7";

        public const string Grounded = "A1";
        public const string Named = "A2";
        public const string UnitSane = "A3";
        public const string NoWorse = "A4";
        public const string Idempotent = "A5";
        public const string Plausible = "A6";
        public const string InSourceOrder = "O1";

        public const decimal KcalLow = 30m;
        public const decimal KcalHigh = 2500m;

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
            ["half"] = 0.5m, ["quarter"] = 0.25m, ["third"] = 1m / 3m, ["fourth"] = 0.25m, ["sixth"] = 1m / 6m, ["eighth"] = 0.125m,
            ["couple"] = 2, ["pair"] = 2, ["dozen"] = 12,
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

        private static readonly Regex Token = new(
            @"(?<number>(?:\d+(?:[.,]\d+)?)?\s*(?:-\s*)?(?:\d+\s*[/⁄]\s*\d+|[¼½¾⅓⅔⅛⅜⅝⅞⅕⅖⅗⅘⅙⅚])|\d+(?:[.,]\d+)?)|(?<word>\p{L}+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RangeJoin = new(@"^\s*(?:-|–|to|or)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { "a", "an", "un", "uno", "una" };

        private static readonly Regex Measure = new(
            @"\b(?:cups?|cupfuls?|tea-?cups?|tumblers?|glass(?:es|fuls?)?|spoons?|spoonfuls?|tablesp\w*|teasp\w*|tbsp?s?|tsps?|tbp|dessertsp\w*|pints?|quarts?|qts?|gallons?|gills?|jills?|pounds?|lbs?|ounces?|oz|grams?|grammi|gr|kilo\w*|kg|litres?|liters?|litro|ml|pinch(?:es)?|dash(?:es)?)\b\.?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] Volume = { "Cup", "Tablespoon", "Teaspoon", "Milliliter", "Liter", "Pint", "Quart", "Gallon", "Fluid Ounce" };

        private static readonly HashSet<string> Liquids = new(StringComparer.OrdinalIgnoreCase)
        {
            "water", "milk", "cream", "broth", "stock", "wine", "vinegar", "oil", "juice", "syrup", "brandy", "rum", "sherry", "beer",
            "cider", "buttermilk", "molasses", "honey", "liquor", "whisky", "whiskey", "vodka", "gin", "acqua", "latte", "brodo", "olio", "vino",
        };

        private static readonly Dictionary<string, decimal> CupsPer = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cup"] = 1m, ["Tablespoon"] = 1m / 16m, ["Teaspoon"] = 1m / 48m, ["Milliliter"] = 1m / 236.588m, ["Liter"] = 1000m / 236.588m,
            ["Pint"] = 2m, ["Quart"] = 4m, ["Gallon"] = 16m, ["Fluid Ounce"] = 0.125m,
        };

        public const decimal MaxSeasoningCups = 4m;
        public const decimal MaxDryStapleCups = 16m;

        private static readonly Regex Seasoning = new(
            @"^(?:(?:ground|fine|finely|table|kosher|sea|coarse|black|white|dry|dried|active\s+dry|instant|powdered)\s+)*" +
            @"(?:salt|pepper|cayenne(?:\s+pepper)?|baking\s+(?:powder|soda)|bicarbonate\s+of\s+soda|saleratus|cream\s+of\s+tartar|yeast|" +
            @"cinnamon|nutmeg|cloves|mace|allspice|paprika|cumin|turmeric|ginger|(?:curry|chili|chilli|garlic|onion|mustard)\s+powder)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DryStaple = new(
            @"^(?:(?:white|brown|whole[- ]wheat|wheat|rye|plain|all[- ]purpose|self[- ]rising|granulated|powdered|confectioners'?|caster|loaf|corn|yellow|rolled|dry|fine|bread)\s+)*" +
            @"(?:flour|sugars?|meal|cornmeal|oatmeal|oats|rice|crumbs|breadcrumbs|cornstarch|starch|cocoa|farina|semolina)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ClassWords = new(StringComparer.OrdinalIgnoreCase) { "spices", "leavening agents", "cereals" };

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
        /// A1: whether the amount stands in one of the texts: equal, within a hundredth, to a number
        /// it states, or to one times the factor of an old measure that text names (a quart is 4
        /// cups, a gill half a cup, a pound 16 ounces), or to a number of a range it states ("2 to 3",
        /// "2-3", "2 or 3"), or to two numbers it states one after the other times or plus each other,
        /// the sum also times a measure's factor ("2 cans of 15 oz", "a pint and a half" is 3 cups), or to pounds and ounces it states added up in ounces, alone or times a
        /// number stated before them ("1 lb. 13 oz. can" is 29, "2 cans (1 lb. 4 oz. each)" 40), or to two
        /// measures joined by "and" added up, in the smaller measure or in the first ("one quart and a pint"
        /// is 6 cups, "1 pound and 2 ounces" 1.125 pounds). "A" or "an" counts one only before a measure, a can or a word of
        /// <paramref name="named"/> (the food), so a stray article grounds nothing, and a measure the text opens with
        /// counts one ("pinch of salt", "tablespoonful grated onion"); a size word may stand before the measure ("a scant
        /// teaspoonful", "a large tablespoon", "scant cup of water"). A number that names another recipe of the book ("al N. 579",
        /// "du N. 579") is no amount, nor is a price ("twenty cents' worth").
        /// </summary>
        public static bool GroundedIn(decimal quantity, IEnumerable<string?> texts, IEnumerable<string?>? named = null)
        {
            if (quantity <= 0) return true;
            var food = (named ?? Array.Empty<string?>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
            foreach (var text in texts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => Price.Replace(CrossReference.Replace(t!, " "), " ")))
            {
                var stated = Stated(text, food);
                var factors = Factors.Where(f => f.Word.IsMatch(text)).Select(f => f.Factor).Prepend(1m).ToList();
                if (stated.Any(n => factors.Any(f => Near(n.Value * f, quantity)))) return true;
                for (var i = 0; i + 1 < stated.Count; i++)
                {
                    var (a, b) = (stated[i].Value, stated[i + 1].Value);
                    if (stated[i + 1].Range && quantity >= Math.Min(a, b) && quantity <= Math.Max(a, b)) return true;
                    if (Near(a * b, quantity) || factors.Any(f => Near((a + b) * f, quantity))) return true;
                }
                foreach (Match m in AndMeasure.Matches(text))
                {
                    if (!Measure.IsMatch(m.Groups["u1"].Value) || !Measure.IsMatch(m.Groups["u2"].Value)) continue;
                    if (Amount(m.Groups["a"].Value) is not { } a || Amount(m.Groups["b"].Value) is not { } b) continue;
                    var (fa, fb) = (FactorOf(m.Groups["u1"].Value), FactorOf(m.Groups["u2"].Value));
                    var total = a * fa + b * fb;
                    if (Near(total, quantity) || Near(total / fa, quantity)) return true;
                }
                foreach (Match m in PoundsAndOunces.Matches(text))
                {
                    var ounces = decimal.Parse(m.Groups["lb"].Value, CultureInfo.InvariantCulture) * 16m + decimal.Parse(m.Groups["oz"].Value, CultureInfo.InvariantCulture);
                    if (Near(ounces, quantity) || Stated(text[..m.Index], food).Any(n => Near(n.Value * ounces, quantity))) return true;
                }
            }
            return false;
        }

        private static readonly Regex CrossReference = new(
            @"\b(?:al|allo|alla|del|nel|dal|vedi|du|au|voir|voyez)\s+N\.\s*\d+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Price = new(
            @"(?:\b[\p{L}\d/-]+\s+)?\b(?:cents?|pence|penny|pennies|dimes?|nickels?|shillings?|sixpence|sixpenny)(?:'s|s')?'?\s+worth\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string AmountText = @"\d+(?:[.,]\d+)?|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve";

        private static readonly Regex AndMeasure = new(
            @"\b(?<a>" + AmountText + @")\s+(?<u1>[\p{L}-]+)\.?\s+and\s+(?<b>" + AmountText + @")\s+(?<u2>[\p{L}-]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static decimal? Amount(string text) => Read(text) ?? (Words.TryGetValue(text, out var word) ? word : null);

        private static decimal FactorOf(string unit) => Factors.FirstOrDefault(f => f.Word.IsMatch(unit)) is { Word: not null } found ? found.Factor : 1m;

        private static readonly Regex PoundsAndOunces = new(
            @"(?<lb>\d+)\s*(?:pounds?|lbs?)\.?[\s,]*(?:and\s+)?(?<oz>\d+(?:\.\d+)?)\s*(?:ounces?|ozs?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static List<(decimal Value, bool Range)> Stated(string text, IReadOnlyList<string> food)
        {
            var found = new List<(decimal Value, bool Range)>();
            if (Measure.Match(Unsized(text.TrimStart())) is { Success: true, Index: 0 }) found.Add((1m, false));
            var matches = Token.Matches(text).ToList();
            var lastEnd = -1;
            for (var i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                decimal? value = null;
                if (m.Groups["number"].Success)
                {
                    value = Numbers(m.Value).FirstOrDefault();
                }
                else if (Articles.Contains(m.Value))
                {
                    var rest = Unsized(text[(m.Index + m.Length)..].TrimStart());
                    var ahead = string.Join(" ", matches.Skip(i + 1).Take(2).Where(n => n.Groups["word"].Success).Select(n => n.Value));
                    if (Measure.Match(rest) is { Success: true, Index: 0 } || Factors.Any(f => f.Word.Match(rest) is { Success: true, Index: 0 }) || CanWord.IsMatch(rest)
                        || (ahead.Length > 0 && food.Any(f => FoodNameMatcher.NamesFood(ahead, f))))
                        value = 1;
                }
                else if (Words.TryGetValue(m.Value, out var word))
                {
                    value = word;
                }
                if (value is not { } v || v <= 0) continue;
                var range = lastEnd >= 0 && RangeJoin.IsMatch(text[lastEnd..m.Index]);
                found.Add((v, range));
                lastEnd = m.Index + m.Length;
            }
            return found;
        }

        private static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.01m;

        private const string SizeText = @"(?:(?:scant|heaping|heaped|large|small|level|good|full|rounded|generous|big|even)\s+)*";

        private static readonly Regex Size = new("^" + SizeText, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex CanWord = new(@"^cans?\b(?!\s+opener)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string Unsized(string text) => Size.Replace(text, string.Empty, 1);

        private const string MeasureText =
            @"cups?|cupfuls?|tea-?cups?\w*|tumblers?|glass(?:es|fuls?)?|wine-?glass\w*|spoons?|spoonfuls?|tablesp\w*|teasp\w*|tbsp?s?|tsps?|tbp|dessertsp\w*|salt-?spoon\w*|" +
            @"pints?|quarts?|qts?|gallons?|gills?|jills?|pounds?|lbs?|ounces?|oz|grams?|kilo\w*|kg|litres?|liters?|ml|pinch(?:es)?|dash(?:es)?|ponies|pony|cans?";

        private static readonly Regex CountOfMeasure = new(
            @"\b(?<n>" + AmountText + @")\s+" + SizeText + @"(?:" + MeasureText + @")\b\.?(?<rest>(?:\s+[\p{L}'’-]+){1,6})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex MeasureAndPart = new(
            @"\b(?<a>" + AmountText + @")\s+" + SizeText + @"(?:" + MeasureText + @")\b\.?\s+and\s+(?:a\s+|one[\s-]+)?(?<b>half|quarter|third)\b(?<rest>(?:\s+[\p{L}'’-]+){1,6})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool NamesAny(string text, IReadOnlyList<string> named) => named.Any(n => FoodNameMatcher.NamesFood(text, n));

        private static List<string> Naming(IEnumerable<string?> named) =>
            named.Where(n => !string.IsNullOrWhiteSpace(n) && !FoodNameMatcher.NamesFood(string.Empty, n)).Select(n => n!).Distinct().ToList();

        /// <summary>
        /// Whether a count stands in one of the texts as the number of a measure of the food ("a large tablespoon of
        /// butter", "two large spoonfuls of butter", "1 tbp chopped pickle", "one pony of maraschino"): the number,
        /// with any size word, is followed by a measure, and the next words name the food or the line's wording. Such
        /// a count stored in pieces counts the measure, not the food.
        /// </summary>
        public static bool CountsMeasure(decimal count, IEnumerable<string?> texts, IEnumerable<string?> named) =>
            MeasureCounts(count, texts, named).Any();

        /// <summary>
        /// The phrases in which a count stands in the texts as the number of a measure of the food, as <see cref="CountsMeasure"/>
        /// reads them ("two large spoonfuls of butter"), from the number to the words naming the food.
        /// </summary>
        public static IEnumerable<string> MeasureCounts(decimal count, IEnumerable<string?> texts, IEnumerable<string?> named)
        {
            var food = Naming(named);
            if (food.Count == 0) return Enumerable.Empty<string>();
            return texts.Where(t => !string.IsNullOrWhiteSpace(t)).SelectMany(text => CountOfMeasure.Matches(text!))
                .Where(m => Amount(m.Groups["n"].Value) is { } n && Near(n, count) && NamesAny(m.Groups["rest"].Value, food))
                .Select(m => m.Value)
                .ToList();
        }

        private static readonly Regex BareCount = new(
            @"(?<![\p{L}\d/.,])(?<n>\d+(?:[.,]\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+" + SizeText + @"(?<rest>[\p{L}'’-]+(?:\s+[\p{L}'’-]+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex MeasureFirst = new(@"^(?:" + MeasureText + @"|of)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether a count stands in one of the texts as a bare count of the food ("Beat 2 eggs"): a number or number word, with
        /// any size word, followed by words naming the food or the line's wording, with no measure between them.
        /// </summary>
        public static bool CountsFood(decimal count, IEnumerable<string?> texts, IEnumerable<string?> named)
        {
            var food = Naming(named);
            return food.Count > 0 && texts.Where(t => !string.IsNullOrWhiteSpace(t)).Any(text => BareCount.Matches(text!)
                .Any(m => Amount(m.Groups["n"].Value) is { } n && Near(n, count) && !MeasureFirst.IsMatch(m.Groups["rest"].Value)
                    && NamesAny(m.Groups["rest"].Value, food)));
        }

        /// <summary>
        /// Whether <paramref name="part"/> is the first part of an amount the texts state as a measure "and a half",
        /// "and a quarter" or "and a third" of the food ("a pound and a half of grated bread" is 1.5 pounds, not 1),
        /// <paramref name="whole"/> being that amount in the same measure.
        /// </summary>
        public static bool AddsPart(decimal part, decimal whole, IEnumerable<string?> texts, IEnumerable<string?> named)
        {
            var food = Naming(named);
            if (whole <= part || food.Count == 0) return false;
            foreach (var text in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                foreach (Match m in MeasureAndPart.Matches(text!))
                {
                    if (Amount(m.Groups["a"].Value) is not { } a || !Near(a, part) || !Words.TryGetValue(m.Groups["b"].Value, out var b)) continue;
                    if (Near(a + b, whole) && NamesAny(m.Groups["rest"].Value, food)) return true;
                }
            }
            return false;
        }

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

        /// <summary>Whether the text names a measure of volume or weight, a pinch or a dash ("cup", "pints", "oz.", "grammi").</summary>
        public static bool NamesMeasure(string? text) => Measure.IsMatch(text ?? string.Empty);

        private static readonly Regex SentenceBreak = new(
            @"(?<=[;!?]|(?<!\b(?:lb|lbs|oz|ozs|pt|pts|qt|qts|doz|tsp|tsps|tbsp|tbsps|gal|teaspn|tablespn|(?-i:[A-Z])))\.)\s+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The method sentences a fill may come from: those that name the line's food or the line's own wording (a
        /// line "sweetbreads" on a USDA food named "Veal, ..., thymus"); the point of a measure's abbreviation ("1/2
        /// pt.") or of an initial ("J. P. Smith's") ends no sentence.
        /// </summary>
        public static IEnumerable<string> MethodSentences(IEnumerable<string> steps, string? food, string raw)
        {
            var named = Naming(new[] { food, Wording(raw) });
            return steps.SelectMany(s => SentenceBreak.Split(s ?? string.Empty))
                .Where(s => named.Count == 0 ? food == null && FoodNameMatcher.NamesFood(s, raw) : NamesAny(s, named));
        }

        /// <summary>The food words of a line's first part, its amount and measure left out ("one pony of maraschino" is "maraschino").</summary>
        public static string? Wording(string? raw)
        {
            var first = (raw ?? string.Empty).Split(FoldedLine.Separator, 2)[0];
            var name = IngredientNameNormalizer.Normalize(first);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>The checks of A1 to A3 that fail on one line, by code; empty when all pass.</summary>
        public static IReadOnlyList<string> Failures(Line line, IEnumerable<string>? method = null)
        {
            var failures = new List<string>();
            if (line.Kind != null && line.Kind != "ingredient" && line.Quantity == 0) return failures;

            var texts = new List<string?> { line.Raw };
            if (line.FilledFromMethod && method != null) texts.AddRange(MethodSentences(method, line.Food, line.Raw));
            if (!GroundedIn(line.Quantity, texts, new[] { line.Food }) || (line.Quantity == 0 && line.Kind is null or "ingredient" && StatesAmount(line.Raw))) failures.Add(Grounded);

            if (line.FoodChanged && line.Kind is null or "ingredient" && !FoodNameMatcher.NamesFood(line.Raw, line.Food)) failures.Add(Named);

            if (line.Quantity > 0 && !UnitSaneFor(line)) failures.Add(UnitSane);
            return failures;
        }

        /// <summary>A line as A6 reads it: its weight in grams (null when it has none), its food's kcal per 100 g and its kind.</summary>
        public sealed record Portion(decimal? Grams, decimal? KcalPer100Grams, string? Kind);

        /// <summary>The kcal per serving of a recipe's lines, or null when its servings are unknown or no line counts.</summary>
        public static decimal? KcalPerServing(IEnumerable<Portion> lines, decimal? servings)
        {
            if (servings is not > 0) return null;
            var counted = lines.Where(l => l.Kind is null or "ingredient" && l.Grams > 0 && l.KcalPer100Grams != null).ToList();
            return counted.Count == 0 ? null : counted.Sum(l => l.Grams!.Value * l.KcalPer100Grams!.Value / 100m) / servings.Value;
        }

        /// <summary>A6: the kcal per serving after the rebuild lands in 30 to 2,500, or no further from that band than before; true when it cannot be read.</summary>
        public static bool KcalPlausible(decimal? before, decimal? after) =>
            after is not { } a || OutOfBand(a) == 0 || (before is { } b && OutOfBand(a) <= OutOfBand(b));

        private static decimal OutOfBand(decimal kcal) => kcal < KcalLow ? KcalLow - kcal : kcal > KcalHigh ? kcal - KcalHigh : 0;

        /// <summary>
        /// O1: whether the rows of an order restore, read in their new order, follow the source's
        /// lines: each row's text (for a " + " fold, one of its parts) is found on a source line
        /// after the line the row before it was found on.
        /// </summary>
        public static bool FollowsSource(IReadOnlyList<string> sourceLines, IReadOnlyList<string> rowsInOrder)
        {
            var keys = sourceLines.Select(FoldedLine.Key).ToList();
            var at = -1;
            foreach (var row in rowsInOrder)
            {
                var own = row.Split(FoldedLine.Separator).Select(FoldedLine.Key).Prepend(FoldedLine.Key(row)).Where(k => k.Length > 0).ToHashSet();
                var next = keys.FindIndex(at + 1, k => own.Contains(k));
                if (next < 0) return false;
                at = next;
            }
            return true;
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
            if (line.Unit != null && CupsPer.TryGetValue(line.Unit, out var perCup))
            {
                var parts = (line.Food ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
                var food = parts.Length > 1 && ClassWords.Contains(parts[0]) ? parts[1] : parts[0];
                var cups = line.Quantity * perCup;
                if (Seasoning.IsMatch(food) && cups > MaxSeasoningCups) return false;
                if (DryStaple.IsMatch(food) && cups > MaxDryStapleCups) return false;
            }
            return true;
        }
    }
}
