using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>A quantity and the NOM measurement it is expressed in, after unit conversion.</summary>
    public sealed record ParsedQuantity(decimal Quantity, string MeasurementName);

    /// <summary>
    /// Deterministic quantity/unit extraction from an ingredient line ("1 ⅔ cups bread flour",
    /// "2 large eggs"), mapped onto NOM's measurement vocabulary. Units NOM has no row for are
    /// converted (quart → 4 Cup, fl oz → Milliliter) or counted as Piece; a line without a unit
    /// counts pieces. A can, jar or package is read by the size it states ("2 (15 oz) cans" →
    /// 30 Ounce) and as Can or Package when it states none. Two amounts of one food joined by
    /// " + " are added up in the first one's unit ("1/4 cup + 1 tablespoon sugar" → 0.313 Cup), or
    /// not read when their units do not add. Returns null when the line has no leading quantity
    /// ("salt, to taste") or sizes a utensil ("two-quart tin mould").
    /// </summary>
    public static class IngredientLineParser
    {
        public const string Piece = "Piece";
        public const string Can = "Can";
        public const string Package = "Package";

        private static readonly Dictionary<char, decimal> UnicodeFractions = new()
        {
            ['¼'] = 0.25m, ['½'] = 0.5m, ['¾'] = 0.75m,
            ['⅓'] = 1m / 3m, ['⅔'] = 2m / 3m,
            ['⅕'] = 0.2m, ['⅖'] = 0.4m, ['⅗'] = 0.6m, ['⅘'] = 0.8m,
            ['⅙'] = 1m / 6m, ['⅚'] = 5m / 6m,
            ['⅛'] = 0.125m, ['⅜'] = 0.375m, ['⅝'] = 0.625m, ['⅞'] = 0.875m,
        };

        private static readonly (string[] Forms, string Measurement, decimal Factor)[] Units =
        {
            (new[] { "tablespoonfuls", "tablespoonful", "tablespoons", "tablespoon", "tblsps", "tblsp", "tbsps", "tbsp", "tbps", "tbp", "tbls", "tbs", "tbl", "tb", "spoonfuls", "spoonful", "T" }, "Tablespoon", 1m),
            (new[] { "teaspoonfuls", "teaspoonful", "teaspoons", "teaspoon", "tsps", "tsp", "t", "cucchiaini", "cucchiaino" }, "Teaspoon", 1m),
            (new[] { "dessertspoonfuls", "dessertspoonful", "dessertspoons", "dessertspoon" }, "Teaspoon", 2m),
            (new[] { "saltspoonfuls", "saltspoonful", "saltspoons", "saltspoon" }, "Teaspoon", 0.25m),
            (new[] { "teacupfuls", "teacupful", "teacups", "teacup" }, "Cup", 0.5m),
            (new[] { "wineglassfuls", "wineglassful", "wineglasses", "wineglass" }, "Cup", 0.25m),
            (new[] { "coffeecupfuls", "coffeecupful", "tumblerfuls", "tumblerful" }, "Cup", 1m),
            (new[] { "cucchiaiate", "cucchiaiata", "cucchiai", "cucchiaio" }, "Tablespoon", 1m),
            (new[] { "decilitri", "decilitro", "dl" }, "Milliliter", 100m),
            (new[] { "bicchieri", "bicchiere" }, "Cup", 1m),
            (new[] { "spicchi", "spicchio" }, "Clove", 1m),
            (new[] { "pizzichi", "pizzico", "prese", "presa" }, "Pinch", 1m),
            (new[] { "fluid ounces", "fluid ounce", "fl oz", "fl. oz." }, "Milliliter", 29.5735m),
            (new[] { "ounces", "ounce", "ozs", "oz" }, "Ounce", 1m),
            (new[] { "pounds", "pound", "lbs", "lb" }, "Pound", 1m),
            (new[] { "kilograms", "kilogram", "chilogrammi", "chilogrammo", "kgs", "kg" }, "Kilogram", 1m),
            (new[] { "grams", "gram", "grammi", "grammo", "grammes", "gramme", "gr", "g" }, "Gram", 1m),
            (new[] { "milliliters", "millilitres", "milliliter", "millilitre", "ml" }, "Milliliter", 1m),
            (new[] { "liters", "litres", "liter", "litre", "litri", "litro", "l" }, "Liter", 1m),
            (new[] { "gallons", "gallon", "gal" }, "Cup", 16m),
            (new[] { "quarts", "quart", "qts", "qt" }, "Cup", 4m),
            (new[] { "pints", "pint", "pts", "pt" }, "Cup", 2m),
            (new[] { "gills", "gill" }, "Cup", 0.5m),
            (new[] { "cupfuls", "cupful", "cups", "cup", "c" }, "Cup", 1m),
            (new[] { "cloves", "clove" }, "Clove", 1m),
            (new[] { "cans", "can", "tins", "tin" }, Can, 1m),
            (new[] { "packages", "package", "pkgs", "pkg", "packets", "packet", "packs", "pack", "jars", "jar", "bottles", "bottle", "boxes", "box", "bags", "bag",
                     "cartons", "carton", "containers", "container", "tubs", "tub", "envelopes", "envelope", "pouches", "pouch", "tubes", "tube", "sachets", "sachet" }, Package, 1m),
            (new[] { "slices", "slice" }, "Slice", 1m),
            (new[] { "stalks", "stalk", "ribs", "rib" }, "Stalk", 1m),
            (new[] { "pinches", "pinch", "dashes", "dash", "splash", "drizzle" }, "Pinch", 1m),
            (new[] { "dozen" }, "Dozen", 1m),
            (new[] { "sticks", "stick", "bunches", "bunch", "sprigs", "sprig", "pieces", "piece", "heads", "head", "ears", "ear",
                     "handfuls", "handful", "knobs", "knob", "large", "medium", "small", "whole" }, Piece, 1m),
        };

        private static readonly Dictionary<string, (string Measurement, decimal Factor)> UnitIndex = BuildUnitIndex();

        private static readonly string UnitPattern = Alternation(Units.SelectMany(u => u.Forms));

        private static string Alternation(IEnumerable<string> forms) =>
            string.Join("|", forms.OrderByDescending(f => f.Length).Select(f => Regex.Escape(f).Replace(@"\ ", @"\s+")));

        private const string Fractions = "¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞";

        private const string Qualifier = @"(?:large|small|medium|level|heaping|heaped|scant|rounded|rounding|generous|good|full|liberal|even|big)";

        private static readonly Regex LineRegex = new(
            @"^\s*(?<qty>\d+\s+\d+\s*/\s*\d+|\d+\s*/\s*\d+|\d+(?:\.\d+)?\s*[" + Fractions + @"]|[" + Fractions + @"]|\d+(?:[.,]\d+)?)" +
            @"(?:\s*(?:-|–|to|or|o)\s*(?:\d+\s+\d+\s*/\s*\d+|\d+\s*/\s*\d+|\d+(?:[.,]\d+)?\s*[" + Fractions + @"]?|[" + Fractions + @"]))?" +
            @"[\s-]*(?:" + Qualifier + @"\s+(?=(?:" + Qualifier + @"\s+)?(?:" + UnitPattern + @")(?![a-z])))*" +
            @"(?<unit>(?:" + UnitPattern + @")(?![a-z]))?\.?(?<rest>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, decimal> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1m, ["an"] = 1m, ["one"] = 1m, ["two"] = 2m, ["three"] = 3m, ["four"] = 4m, ["five"] = 5m,
            ["six"] = 6m, ["seven"] = 7m, ["eight"] = 8m, ["nine"] = 9m, ["ten"] = 10m, ["eleven"] = 11m, ["twelve"] = 12m,
            ["half"] = 0.5m, ["one-half"] = 0.5m, ["one half"] = 0.5m, ["a half"] = 0.5m,
            ["quarter"] = 0.25m, ["a quarter"] = 0.25m, ["one-quarter"] = 0.25m, ["one quarter"] = 0.25m,
            ["three-quarters"] = 0.75m, ["three quarters"] = 0.75m, ["one-third"] = 1m / 3m, ["one third"] = 1m / 3m,
            ["two-thirds"] = 2m / 3m, ["two thirds"] = 2m / 3m,
        };

        private static readonly string NumberWordPattern = string.Join("|",
            NumberWords.Keys.OrderByDescending(k => k.Length).Select(k => Regex.Escape(k).Replace(@"\ ", @"\s+")));

        private static readonly Regex LeadingWords = new(
            @"^\s*(?<a>" + NumberWordPattern + @")(?:\s+and\s+(?<b>" + NumberWordPattern + @"))?(?:\s+of)?(?:\s+an?)?\s+(?!(?:little|few|bit|touch|tad|squeeze|sprinkle|sprinkling|grating|good\s+pinch)\b)(?=\S)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex UnitFirst = new(
            @"^(?:pinch|dash|handful|sprig|clove|stick|knob|splash|drizzle|teacupful|cupful|tablespoonful|teaspoonful|dessertspoonful|saltspoonful|spoonful|wineglassful|wineglass|teacup|gill|pint|quart|pound)\s+of\s+\S",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex HyphenatedMixed = new(@"^\s*(\d+)-(\d+\s*/\s*\d+)", RegexOptions.Compiled);

        private static readonly Regex Trailing = new(
            @"[,(]\s*(?:(?<unit>" + UnitPattern + @")\.?\s*(?<qty>\d+(?:[.,]\d+)?)|(?<qty>\d+(?:[.,]\d+)?)\s*(?<unit>" + UnitPattern + @"))\s*\)?\.?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static ParsedQuantity? Parse(string? rawLine) => Read(rawLine, out _, out _);

        /// <summary>
        /// The amount a line gives by the size of its can, package or piece ("1 (15 oz) can black
        /// beans" → 15 Ounce, "2 cans (15 oz each) corn" → 30 Ounce, "1 (3-pound) chicken" →
        /// 3 Pound), or null when the line states no such size or the size could be each or total
        /// ("4 (6-ounce) salmon filets").
        /// </summary>
        public static ParsedQuantity? StatedSize(string? rawLine) => Read(rawLine, out var sized, out _) is { } parsed && sized ? parsed : null;

        /// <summary>
        /// The number of cans or packages a line states a size for ("1 (15 oz) can chickpeas" →
        /// 1 Can, "2 jars (24 oz each) sauce" → 2 Package), or null when the line states no size or
        /// the size it states is not a can's or a package's ("1 (3-pound) chicken").
        /// </summary>
        public static ParsedQuantity? SizedContainers(string? rawLine) => Read(rawLine, out var sized, out var containers) != null && sized ? containers : null;

        /// <summary>
        /// Whether a line is read as a can or package: it states a size (<see cref="StatedSize"/>)
        /// or its unit is Can or Package ("1 jar marinara").
        /// </summary>
        public static bool ReadsPackage(string? rawLine) =>
            Read(rawLine, out var sized, out _) is { } parsed && (sized || parsed.MeasurementName is Can or Package);

        private static ParsedQuantity? Read(string? rawLine, out bool sized, out ParsedQuantity? containers)
        {
            sized = false;
            containers = null;
            if (string.IsNullOrWhiteSpace(rawLine)) return null;
            rawLine = IngredientNameNormalizer.PlainFractions(rawLine);
            if (IsElliptical(rawLine)) return null;
            if (SizedUtensil.IsMatch(rawLine)) return null;
            if (SumOfAmounts.Match(rawLine) is { Success: true } sum && IsBareAmount(sum.Groups["a"].Value)
                && ParseLeading(sum.Groups["a"].Value, out _, out _) is { } first)
            {
                return Sum(first, Read(sum.Groups["b"].Value, out _, out _));
            }

            var leading = ParseLeading(rawLine, out sized, out containers);
            if (leading is { MeasurementName: not Piece }) return leading;

            var spelled = SpellOut(System.Net.WebUtility.HtmlDecode(rawLine).Trim());
            var respelled = ParseLeading(spelled, out sized, out containers);
            if (respelled is { MeasurementName: not Piece }) return respelled;
            sized = false;
            containers = null;
            if ((leading ?? respelled) is { } counted) return counted;

            var of = PartOf.Match(spelled);
            if (of.Success && ParseQuantity(of.Groups["qty"].Value) is > 0 and var count) return new ParsedQuantity(Round(of.Groups["dozen"].Success ? count * 12 : count), Piece);

            var after = AfterName.Match(spelled);
            if (after.Success && ParseQuantity(after.Groups["qty"].Value) is > 0 and var amount)
            {
                return after.Groups["unit"].Success ? FromUnit(amount, after.Groups["unit"].Value) : new ParsedQuantity(Round(amount), Piece);
            }
            return null;
        }

        private static readonly (Regex Pattern, string Replacement)[] Respellings =
        {
            (new Regex(@"\btable[- ]?spoons?[- ]?full?s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "tablespoonfuls"),
            (new Regex(@"\btea[- ]?spoons?[- ]?full?s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "teaspoonfuls"),
            (new Regex(@"\bsalt[- ]?spoons?(?:[- ]?full?s?)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "saltspoonfuls"),
            (new Regex(@"\btea[- ]?cups?(?:[- ]?full?s?|ful)?\b|\bteacupsful\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "teacupfuls"),
            (new Regex(@"\bwine[- ]?glass(?:es)?(?:[- ]?full?s?)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "wineglassfuls"),
            (new Regex(@"\bcoffee[- ]?cups?(?:[- ]?full?s?)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "coffeecupfuls"),
            (new Regex(@"\btumblers?[- ]?full?s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "tumblerfuls"),
            (new Regex(@"\bcupsful\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "cupfuls"),
            (new Regex(@"\btable[- ]spoons?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "tablespoons"),
            (new Regex(@"\btea[- ]spoons?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "teaspoons"),
            (new Regex(@"\bdessert[- ]spoons?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "dessertspoons"),
            (new Regex(@"\bspoon-?full?s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "spoonfuls"),
            (new Regex(@"\bcup-?full?s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "cupfuls"),
            (new Regex(@"\bhand-?full?s?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "handfuls"),
            (new Regex(@"^\s*(?:from|about|nearly|scant|full|fully|good|generous|heaping|rounded|level|fully)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase), ""),
            (new Regex(@"\b(?<p>dessert|table|tea)?spoonsful\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "${p}spoonfuls"),
            (new Regex(@"\bdessert-?spoons?-?fulls?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "dessertspoonfuls"),
            (new Regex(@"\bhalf[- ]a[- ]dozen\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "6"),
            (new Regex(@"^\s*dozen\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1 dozen"),
            (new Regex(@"^\s*(?:a\s+)?third\s+of\s+an?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1/3"),
            (new Regex(@"\bquarter[- ](?=(?:pound|pint|ounce|cup)s?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1/4 "),
            (new Regex(@"^\s*(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|twelve|twenty)-(?=(?:pound|quart|pint|ounce|inch|gallon|lb|oz)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1 ${n}-"),
            (new Regex(@"\bun\s+quarto\s+di\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1/4 di"),
            (new Regex(@"\b(?:un|uno|una|un')\s+(?!po[co']|pochino|poca)(?=\p{L})", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1 "),
            (new Regex(@"^\s*(?<unit>grammi|grammo|decilitri|litri|chilogrammi)\s+(?<q>\d+(?:[.,]\d+)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "${q} ${unit}"),
            (new Regex(@"\b(?:a\s+)?quarter\s+of\s+an?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1/4"),
            (new Regex(@"\bhalf[- ](?:an?[- ])?(?=(?:pound|pint|quart|cup|ounce|gill|gallon|dozen|teaspoon|tablespoon)s?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1/2 "),
            (new Regex(@"(?<![\p{L}\d]\s)(?<=^\s*|\ba\s|[,(]\s?)(?:a\s+)?(?<unit>pound|pint|quart|cup|ounce|gill)s?\s+and\s+a\s+half\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1 1/2 ${unit}"),
            (new Regex(@"\b(?:mezz[oa]|half)[- ]an?\b|\bmezz[oa]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "1/2"),
        };

        private static readonly Dictionary<string, int> Ones = new(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
            ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
            ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
            ["due"] = 2, ["tre"] = 3, ["quattro"] = 4, ["cinque"] = 5, ["sei"] = 6, ["sette"] = 7, ["otto"] = 8, ["nove"] = 9,
            ["dieci"] = 10, ["dodici"] = 12,
        };

        private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        };

        private static readonly Dictionary<string, int> Denominators = new(StringComparer.OrdinalIgnoreCase)
        {
            ["half"] = 2, ["halves"] = 2, ["third"] = 3, ["thirds"] = 3, ["fourth"] = 4, ["fourths"] = 4, ["quarter"] = 4, ["quarters"] = 4,
            ["fifth"] = 5, ["fifths"] = 5, ["sixth"] = 6, ["sixths"] = 6, ["eighth"] = 8, ["eighths"] = 8,
        };

        private static readonly Regex FractionWords = new(
            @"\b(?<n>one|two|three|four|five|six|seven|a)[- ](?<d>halves|half|thirds?|fourths?|quarters?|fifths?|sixths?|eighths?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CompoundNumber = new(
            @"\b(?<t>twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:[- ](?<o>one|two|three|four|five|six|seven|eight|nine))?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OnesWord = new(
            @"\b(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|due|tre|quattro|cinque|sei|sette|otto|nove|dieci|dodici)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NumberedAndAHalf = new(
            @"\b(?<q>\d+)\s+(?<unit>pound|pint|quart|cup|ounce|gill)s?\s+and\s+(?:a\s+half|1/2)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AndFraction = new(@"\b(?<w>\d+)\s+and\s+(?:a\s+)?(?<f>\d+/\d+|half|quarter)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string SpellOut(string line)
        {
            foreach (var (pattern, replacement) in Respellings) line = pattern.Replace(line, replacement);
            line = FractionWords.Replace(line, m =>
                $"{(m.Groups["n"].Value.Equals("a", StringComparison.OrdinalIgnoreCase) ? 1 : Ones[m.Groups["n"].Value])}/{Denominators[m.Groups["d"].Value]}");
            line = CompoundNumber.Replace(line, m => (Tens[m.Groups["t"].Value] + (m.Groups["o"].Success ? Ones[m.Groups["o"].Value] : 0)).ToString(CultureInfo.InvariantCulture));
            line = OnesWord.Replace(line, m => Ones[m.Value].ToString(CultureInfo.InvariantCulture));
            line = NumberedAndAHalf.Replace(line, "${q} 1/2 ${unit}s");
            line = AndFraction.Replace(line, m => m.Groups["w"].Value + " " + m.Groups["f"].Value.ToLowerInvariant() switch
            {
                "half" => "1/2",
                "quarter" => "1/4",
                var f => f,
            });
            line = Regex.Replace(line, @"^\s*(?<q>\d+/\d+)\s+an?\s+(?=\p{L})", "${q} ");
            return Regex.Replace(line, @"\b(?<q>[\d/ ]*\d)\s+of\s+an?\s+(?=\p{L})", "${q} ");
        }

        private static readonly Regex Elliptical = new(
            @"^\s*(?:\d+(?:[.,]\d+)?(?:\s+\d+/\d+)?|\d+/\d+|an?|one|two|three|four|five|six|seven|eight|nine|ten|twelve|half)\s+(?:" + Qualifier + @"\s+)?(?:ones?\s+)?of\s+(?!an?\b|the\b|each\b)\p{L}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// A line that borrows its unit from the line before it ("two of butter" after "one cup of
        /// sugar"): the number is there but what it counts is not, so it is no quantity at all.
        /// </summary>
        public static bool IsElliptical(string? rawLine) => rawLine != null && Elliptical.IsMatch(rawLine);

        private const string QuantityText = @"\d+\s+\d+/\d+|\d+/\d+|\d+(?:[.,]\d+)?";

        private static readonly Regex PartOf = new(
            @"^\s*(?:the\s+)?(?:[\p{L}-]+\s+){0,3}?(?:juice|yolks?|whites?|peel|rinds?|zest|fillets?|meat|flesh|leaves|hearts?|livers?|tongues?|breasts?|legs?|wings?|soft\s+part|sugo)\s+(?:of|di)\s+(?:about\s+)?(?<qty>" + QuantityText + @")\s+(?<dozen>dozen\s+(?:of\s+)?)?(?:[\p{L}-]+\s+){0,3}\p{L}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AfterName = new(
            @"^\s*\p{L}[^\d(]*?(?:,\s*|\(\s*|\s)(?<qty>" + QuantityText + @")\s*(?:(?<unit>(?:" + UnitPattern + @"))(?![a-z])\.?)?\s*\)?\s*\.?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] LargeVolumes = { "gallons", "quarts", "pints" };

        private static readonly string SizeUnit = Alternation(Units
            .Where(u => u.Measurement is "Ounce" or "Pound" or "Gram" or "Kilogram" or "Milliliter" or "Liter" || LargeVolumes.Contains(u.Forms[0]))
            .SelectMany(u => u.Forms));

        private const string Utensil = @"(?:moulds?|molds?|pans?|saucepans?|stewpans?|skillets?|dish(?:es)?|basins?|bowls?|kettles?|casseroles?|ramekins?)\b";

        private static readonly string Container = @"(?:" + Alternation(Units.Where(u => u.Measurement is Can or Package).SelectMany(u => u.Forms)) + @")(?![a-z])\.?(?!\s*" + Utensil + ")";

        private static readonly Regex SizedUtensil = new(
            @"^[^,(]*?\b(?:quarts?|pints?|gallons?|inch(?:es)?|cups?)\b[\s-]*(?:[\p{L}-]+\s+){0,2}?" + Utensil,
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SumOfAmounts = new(
            @"^\s*(?<a>[^+]*?)\s+\+\s+(?<b>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, (string Family, decimal Base)> Summable = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Teaspoon"] = ("volume", 1m), ["Tablespoon"] = ("volume", 3m), ["Cup"] = ("volume", 48m),
            ["Ounce"] = ("weight", 1m), ["Pound"] = ("weight", 16m),
            ["Gram"] = ("metric weight", 1m), ["Kilogram"] = ("metric weight", 1000m),
            ["Milliliter"] = ("metric volume", 1m), ["Liter"] = ("metric volume", 1000m),
        };

        private const string Number =
            @"\d+\s+\d+\s*/\s*\d+|\d+\s*/\s*\d+|\d+(?:[.,]\d+)?\s*[" + Fractions + @"]|[" + Fractions + @"]|\d+(?:[.,]\d+)?|\.\d+";

        private const string Count = @"(?>(?<count>" + Number + @")(?:\s*(?:-|–|to|or)\s*(?:" + Number + @"))?)";

        private const string ContainerQualifier = @"(?:(?:large|small|medium|big|regular|standard|whole|full|tall|jumbo|(?:family|large|medium|small)[- ]sized?)\s+)*";

        private static readonly string Size =
            @"(?:(?<about>about|approx\.?|approximately)\s+)?(?<sq>" + Number + @")(?:\s*(?:-|–|to)\s*(?:" + Number + @"))?(?<hy>[\s-]*)" +
            @"(?<su>" + SizeUnit + @")(?![a-z])(?!(?<=pounds?|lbs?)\.?[\s,]*(?:and\s+)?(?:" + Number + @")[\s-]*(?:ounces?|ozs?)(?![a-z]))\.?" +
            @"(?:\s*/\s*(?:" + Number + @")[\s-]*(?:" + SizeUnit + @")(?![a-z])\.?)?";

        private const string EachOrTotal = @"(?:\s+(?:(?<each>each|apiece)|(?<total>total)|size))?";

        private static readonly Regex SizeFirst = new(
            @"^\s*" + Count + @"\s*(?:x\s*)?" + ContainerQualifier +
            @"(?:(?<open>\(\(?\s*)" + Size + @"(?<each>\s+each)?(?<close>\s*\)\)?)?" +
            @"|" + Size + @"(?<each>\s+each)?(?!\s*\)))" +
            @"[\s,-]*" + ContainerQualifier +
            @"(?:(?<con>" + Container + @")|(?<measure>(?:cups?|c\.|tablespoons?|tbsps?|tbs|teaspoons?|tsps?|pints?|quarts?)(?![a-z]))|(?<food>\p{L}))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SizeAfterContainer = new(
            @"^\s*" + Count + @"\s+" + ContainerQualifier + Container +
            @"(?:\s*\(\(?\s*" + Size + EachOrTotal +
            @"|\s*,\s*" + Size + EachOrTotal + @"(?!\s*\))" +
            @"|\s+" + Size + EachOrTotal + @"(?!\s*\)))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SizeLater = new(
            @"^\s*" + Count + @"\s+" + ContainerQualifier + Container + @"[^()]*\(\(?\s*" + Size +
            @"(?:\s+(?:(?<each>each|apiece|size|" + Container + @")|(?<total>total)))?\s*[,;)]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex MixedHyphen = new(@"(?<=\d)-(?=\d+\s*/\s*\d)", RegexOptions.Compiled);

        private static readonly Dictionary<string, decimal> GramsOrMillilitersPer = new()
        {
            ["Ounce"] = 28.3495m, ["Pound"] = 453.592m, ["Gram"] = 1m, ["Kilogram"] = 1000m, ["Milliliter"] = 1m, ["Liter"] = 1000m, ["Cup"] = 236.588m,
        };

        private const decimal LargestStatedSize = 15000m;

        private static ParsedQuantity? SizeIn(string line, out ParsedQuantity? containers)
        {
            containers = null;
            line = MixedHyphen.Replace(line, " ");
            var first = SizeFirst.Match(line);
            if (first.Success)
            {
                var marked = first.Groups["open"].Success || first.Groups["hy"].Value.Contains('-');
                var one = first.Groups["each"].Success || ParseQuantity(first.Groups["count"].Value) == 1m;
                if (first.Groups["con"].Success) return Sized(first, each: true, out containers);
                if (first.Groups["measure"].Success && first.Groups["close"].Success) return Sized(first, each: false, out containers);
                if (marked && one && Sized(first, each: true, out containers) is { MeasurementName: "Ounce" or "Pound" or "Gram" or "Kilogram" } weighed) return weighed;
                containers = null;
            }

            var after = SizeAfterContainer.Match(line);
            if (after.Success)
            {
                var whole = ParseQuantity(after.Groups["count"].Value) is { } count && count == decimal.Truncate(count);
                var each = after.Groups["each"].Success
                    || (!after.Groups["total"].Success && whole && !after.Groups["about"].Success);
                return Sized(after, each, out containers);
            }

            var later = SizeLater.Match(line);
            if (later.Success && (later.Groups["each"].Success || later.Groups["total"].Success || ParseQuantity(later.Groups["count"].Value) == 1m))
            {
                return Sized(later, each: !later.Groups["total"].Success, out containers);
            }
            return null;
        }

        private static readonly Regex ContainerWord = new(@"(?<!\p{L})" + Container, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static ParsedQuantity? Sized(Match match, bool each, out ParsedQuantity? containers)
        {
            containers = null;
            var count = ParseQuantity(match.Groups["count"].Value);
            var size = ParseQuantity(match.Groups["sq"].Value);
            if (count is not > 0 || size is not > 0) return null;

            var word = match.Groups["con"].Success ? match.Groups["con"].Value : ContainerWord.Match(match.Value) is { Success: true } named ? named.Value : null;
            if (word != null) containers = FromUnit(count.Value, word.Trim().TrimEnd('.'));

            var sized = FromUnit(each ? count.Value * size.Value : size.Value, match.Groups["su"].Value);
            return GramsOrMillilitersPer.TryGetValue(sized.MeasurementName, out var per) && sized.Quantity > 0 && sized.Quantity * per <= LargestStatedSize
                ? sized
                : null;
        }

        private static ParsedQuantity? ParseLeading(string rawLine, out bool sized, out ParsedQuantity? containers)
        {
            sized = false;
            containers = null;
            var line = HyphenatedMixed.Replace(System.Net.WebUtility.HtmlDecode(rawLine).Trim(), "$1 $2");
            if (IsElliptical(line)) return null;
            if (UnitFirst.IsMatch(line)) line = "1 " + line;
            var words = LeadingWords.Match(line);
            if (words.Success)
            {
                var value = NumberWords[Regex.Replace(words.Groups["a"].Value, @"\s+", " ")]
                    + (words.Groups["b"].Success ? NumberWords[Regex.Replace(words.Groups["b"].Value, @"\s+", " ")] : 0m);
                line = value.ToString(CultureInfo.InvariantCulture) + " " + line[words.Length..];
            }

            if (SizeIn(line, out var sizedContainers) is { } stated)
            {
                sized = true;
                containers = sizedContainers;
                return stated;
            }

            var trailing = Trailing.Match(line);
            if (trailing.Success && !char.IsDigit(line[0]) && !UnicodeFractions.ContainsKey(line[0]))
            {
                var tq = ParseQuantity(trailing.Groups["qty"].Value);
                if (tq is > 0) return FromUnit(tq.Value, trailing.Groups["unit"].Value);
            }

            var match = LineRegex.Match(line);
            if (!match.Success || (match.Groups["rest"].Value.Trim().Length == 0 && !match.Groups["unit"].Success)) return null;

            var quantity = ParseQuantity(match.Groups["qty"].Value);
            if (quantity is not > 0) return null;

            return FromUnit(quantity.Value, match.Groups["unit"].Success ? match.Groups["unit"].Value : null);
        }

        private static bool IsBareAmount(string text) =>
            LineRegex.Match(text.Trim()) is { Success: true } m && m.Groups["unit"].Success && m.Groups["rest"].Value.Trim().TrimEnd('.').Length == 0;

        private static ParsedQuantity? Sum(ParsedQuantity first, ParsedQuantity? second)
        {
            if (second == null || !Summable.TryGetValue(first.MeasurementName, out var a) || !Summable.TryGetValue(second.MeasurementName, out var b) || a.Family != b.Family) return null;
            return new ParsedQuantity(Round((first.Quantity * a.Base + second.Quantity * b.Base) / a.Base), first.MeasurementName);
        }

        private static ParsedQuantity FromUnit(decimal quantity, string? unit)
        {
            if (string.IsNullOrEmpty(unit)) return new ParsedQuantity(Round(quantity), Piece);

            var key = Regex.Replace(unit, @"\s+", " ");
            return UnitIndex.TryGetValue(key, out var mapped) || UnitIndex.TryGetValue(key.ToLowerInvariant(), out mapped)
                ? new ParsedQuantity(Round(quantity * mapped.Factor), mapped.Measurement)
                : new ParsedQuantity(Round(quantity), Piece);
        }

        /// <summary>
        /// Brings a quantity the scraper already parsed onto NOM's vocabulary: its unit mapped
        /// (with conversion), no unit meaning pieces; a missing quantity is parsed from the raw line.
        /// A count of pieces, cans or packages gives way to the size the raw line states, unless
        /// <paramref name="drained"/> says the food is weighed drained: a can's stated size is its net
        /// weight with the liquid, so such a line stays a count of cans or packages.
        /// </summary>
        public static ParsedQuantity? Normalize(decimal? quantity, string? unit, string? rawLine, bool drained = false)
        {
            if (quantity is not > 0) return drained && SizedContainers(rawLine) is { } counted ? counted : Parse(rawLine);

            var mapped = FromUnit(quantity.Value, string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().TrimEnd('.'));
            if (mapped.MeasurementName is not (Piece or Can or Package) || StatedSize(rawLine) is not { } stated) return mapped;
            return drained ? SizedContainers(rawLine) ?? mapped : stated;
        }

        private static decimal? ParseQuantity(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return null;

            var total = 0m;
            foreach (var fraction in text.Where(UnicodeFractions.ContainsKey))
            {
                total += UnicodeFractions[fraction];
            }
            text = new string(text.Where(c => !UnicodeFractions.ContainsKey(c)).ToArray()).Trim();

            foreach (var part in Regex.Split(text, @"\s+").Where(p => p.Length > 0))
            {
                if (part.Contains('/'))
                {
                    var bits = part.Split('/');
                    if (bits.Length == 2
                        && decimal.TryParse(bits[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var num)
                        && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var den)
                        && den != 0)
                    {
                        total += num / den;
                    }
                }
                else if (decimal.TryParse(part.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var whole))
                {
                    total += whole;
                }
            }
            return total;
        }

        private static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

        private static Dictionary<string, (string, decimal)> BuildUnitIndex()
        {
            var index = new Dictionary<string, (string, decimal)>(StringComparer.Ordinal);
            foreach (var (forms, measurement, factor) in Units)
            {
                foreach (var form in forms)
                {
                    index.TryAdd(form, (measurement, factor));
                    if (form.Length > 1) index.TryAdd(form.ToLowerInvariant(), (measurement, factor));
                }
            }
            return index;
        }
    }
}
