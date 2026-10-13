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
    /// counts pieces. Returns null when the line has no leading quantity ("salt, to taste").
    /// </summary>
    public static class IngredientLineParser
    {
        public const string Piece = "Piece";

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
            (new[] { "tablespoonfuls", "tablespoonful", "tablespoons", "tablespoon", "tbsps", "tbsp", "tbs", "tbl", "spoonfuls", "spoonful", "T" }, "Tablespoon", 1m),
            (new[] { "teaspoonfuls", "teaspoonful", "teaspoons", "teaspoon", "tsps", "tsp", "t", "cucchiaini", "cucchiaino" }, "Teaspoon", 1m),
            (new[] { "dessertspoonfuls", "dessertspoonful", "dessertspoons", "dessertspoon" }, "Teaspoon", 2m),
            (new[] { "cucchiaiate", "cucchiaiata", "cucchiai", "cucchiaio" }, "Tablespoon", 1m),
            (new[] { "decilitri", "decilitro", "dl" }, "Milliliter", 100m),
            (new[] { "bicchieri", "bicchiere" }, "Cup", 1m),
            (new[] { "spicchi", "spicchio" }, "Clove", 1m),
            (new[] { "pizzichi", "pizzico", "prese", "presa" }, "Pinch", 1m),
            (new[] { "fluid ounces", "fluid ounce", "fl oz", "fl. oz." }, "Milliliter", 29.5735m),
            (new[] { "ounces", "ounce", "oz" }, "Ounce", 1m),
            (new[] { "pounds", "pound", "lbs", "lb" }, "Pound", 1m),
            (new[] { "kilograms", "kilogram", "chilogrammi", "chilogrammo", "kgs", "kg" }, "Kilogram", 1m),
            (new[] { "grams", "gram", "grammi", "grammo", "grammes", "gramme", "gr", "g" }, "Gram", 1m),
            (new[] { "milliliters", "millilitres", "milliliter", "millilitre", "ml" }, "Milliliter", 1m),
            (new[] { "liters", "litres", "liter", "litre", "litri", "litro", "l" }, "Liter", 1m),
            (new[] { "gallons", "gallon", "gal" }, "Cup", 16m),
            (new[] { "quarts", "quart", "qt" }, "Cup", 4m),
            (new[] { "pints", "pint", "pt" }, "Cup", 2m),
            (new[] { "gills", "gill" }, "Cup", 0.5m),
            (new[] { "cupfuls", "cupful", "cups", "cup", "c" }, "Cup", 1m),
            (new[] { "cloves", "clove" }, "Clove", 1m),
            (new[] { "cans", "can", "tins", "tin" }, "Can", 1m),
            (new[] { "slices", "slice" }, "Slice", 1m),
            (new[] { "stalks", "stalk", "ribs", "rib" }, "Stalk", 1m),
            (new[] { "pinches", "pinch", "dashes", "dash", "splash", "drizzle" }, "Pinch", 1m),
            (new[] { "dozen" }, "Dozen", 1m),
            (new[] { "sticks", "stick", "packages", "package", "pkgs", "pkg", "packets", "packet", "bunches", "bunch",
                     "sprigs", "sprig", "pieces", "piece", "heads", "head", "ears", "ear", "handfuls", "handful", "knobs", "knob", "large", "medium", "small", "whole" }, Piece, 1m),
        };

        private static readonly Dictionary<string, (string Measurement, decimal Factor)> UnitIndex = BuildUnitIndex();

        private static readonly string UnitPattern = string.Join("|",
            Units.SelectMany(u => u.Forms).OrderByDescending(f => f.Length).Select(f => Regex.Escape(f).Replace(@"\ ", @"\s+")));

        private const string Fractions = "¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞";

        private static readonly Regex LineRegex = new(
            @"^\s*(?<qty>\d+\s+\d+\s*/\s*\d+|\d+\s*/\s*\d+|\d+(?:\.\d+)?\s*[" + Fractions + @"]|[" + Fractions + @"]|\d+(?:[.,]\d+)?)" +
            @"(?:\s*(?:-|–|to|or|o)\s*(?:\d+(?:[.,]\d+)?|[" + Fractions + @"]))?" +
            @"\s*(?<unit>(?:" + UnitPattern + @")(?![a-z]))?\.?(?<rest>.*)$",
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
            @"^(?:pinch|dash|handful|sprig|clove|stick|knob|splash|drizzle)\s+of\s+\S",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex HyphenatedMixed = new(@"^\s*(\d+)-(\d+\s*/\s*\d+)", RegexOptions.Compiled);

        private static readonly Regex Trailing = new(
            @"[,(]\s*(?:(?<unit>" + UnitPattern + @")\.?\s*(?<qty>\d+(?:[.,]\d+)?)|(?<qty>\d+(?:[.,]\d+)?)\s*(?<unit>" + UnitPattern + @"))\s*\)?\.?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static ParsedQuantity? Parse(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return null;
            if (ParseLeading(rawLine) is { } leading) return leading;

            var spelled = SpellOut(System.Net.WebUtility.HtmlDecode(rawLine).Trim());
            if (ParseLeading(spelled) is { } respelled) return respelled;

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
            (new Regex(@"\btable-?spoons?-?fulls?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "tablespoonfuls"),
            (new Regex(@"\btea-?spoons?-?fulls?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "teaspoonfuls"),
            (new Regex(@"\bspoon-?fulls?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "spoonfuls"),
            (new Regex(@"\bcup-?fulls?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "cupfuls"),
            (new Regex(@"\bhand-?fulls?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "handfuls"),
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
            return Regex.Replace(line, @"\b(?<q>[\d/ ]*\d)\s+of\s+an?\s+(?=\p{L})", "${q} ");
        }

        private const string QuantityText = @"\d+\s+\d+/\d+|\d+/\d+|\d+(?:[.,]\d+)?";

        private static readonly Regex PartOf = new(
            @"^\s*(?:the\s+)?(?:[\p{L}-]+\s+){0,3}?(?:juice|yolks?|whites?|peel|rinds?|zest|fillets?|meat|flesh|leaves|hearts?|livers?|tongues?|breasts?|legs?|wings?|soft\s+part|sugo)\s+(?:of|di)\s+(?:about\s+)?(?<qty>" + QuantityText + @")\s+(?<dozen>dozen\s+(?:of\s+)?)?(?:[\p{L}-]+\s+){0,3}\p{L}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AfterName = new(
            @"^\s*\p{L}[^\d(]*?(?:,\s*|\(\s*|\s)(?<qty>" + QuantityText + @")\s*(?:(?<unit>(?:" + UnitPattern + @"))(?![a-z])\.?)?\s*\)?\s*\.?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static ParsedQuantity? ParseLeading(string rawLine)
        {
            var line = HyphenatedMixed.Replace(System.Net.WebUtility.HtmlDecode(rawLine).Trim(), "$1 $2");
            if (UnitFirst.IsMatch(line)) line = "1 " + line;
            var words = LeadingWords.Match(line);
            if (words.Success)
            {
                var value = NumberWords[Regex.Replace(words.Groups["a"].Value, @"\s+", " ")]
                    + (words.Groups["b"].Success ? NumberWords[Regex.Replace(words.Groups["b"].Value, @"\s+", " ")] : 0m);
                line = value.ToString(CultureInfo.InvariantCulture) + " " + line[words.Length..];
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
        /// </summary>
        public static ParsedQuantity? Normalize(decimal? quantity, string? unit, string? rawLine)
        {
            if (quantity is not > 0) return Parse(rawLine);
            if (string.IsNullOrWhiteSpace(unit)) return new ParsedQuantity(Round(quantity.Value), Piece);

            var key = Regex.Replace(unit.Trim().TrimEnd('.'), @"\s+", " ");
            return UnitIndex.TryGetValue(key, out var mapped) || UnitIndex.TryGetValue(key.ToLowerInvariant(), out mapped)
                ? new ParsedQuantity(Round(quantity.Value * mapped.Factor), mapped.Measurement)
                : new ParsedQuantity(Round(quantity.Value), Piece);
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
