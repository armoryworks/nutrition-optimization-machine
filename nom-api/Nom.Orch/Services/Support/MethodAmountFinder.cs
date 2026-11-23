using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Old cookbooks often list an ingredient bare ("butter") and state its amount in the method
    /// ("put two ounces of butter in the saucepan"). Finds that amount when the method states it
    /// exactly once, immediately before the ingredient's name; "the butter", "half the butter" and
    /// conflicting amounts give nothing, and so does a name another line of the recipe also names
    /// ("a little butter" beside "a quarter of a pound of butter"), because the method's amount
    /// cannot then be pinned to one of those lines. The food's other words may stand between the
    /// measure and its last word ("three tablespoonfuls of olive oil"); a line naming its food with an
    /// "or" ("stewed or canned tomatoes") takes the amount the method states before those same words
    /// ("a pint of stewed or canned tomatoes"), while a line joining foods with "and" takes none. A ratio
    /// ("one teacup of salt to a gallon of water", "half vinegar and half water", "to each pint", "in
    /// each tomato", "on each plate", "to a pound of pulp", "to two pounds of cherries", "to one of fruit", "for
    /// half a pint") anywhere in a sentence leaves every amount of that sentence alone ("To every quart allow
    /// ..., half a teaspoonful of mace"), even where it matches the recipe's own amount; a liquid another
    /// ingredient is dissolved, softened, soaked or steeped in gives nothing ("dissolved in one teaspoon of
    /// hot water"); a vessel or a length
    /// ("a kettle of water", "1-inch of water", "a water bath"), washing water ("three or four waters"),
    /// an alternative ("..., or four fresh tomatoes") and a temperature the line contradicts ("cold
    /// vinegar" against "boiling vinegar") give nothing either, and a count of pieces is never read for
    /// a liquid, from a measure ("a handful of parsley"), nor for a food the caller says is not counted
    /// in pieces, though a dozen is a count. A range ("six or eight pounds", "three to five gallons", "ten or a
    /// dozen") is read whole, however many of the food's words follow it, so it gives its lower end, as a
    /// line's range does. The number always comes from <see cref="IngredientLineParser"/>.
    /// </summary>
    public static class MethodAmountFinder
    {
        private static readonly Regex NonLetters = new(@"[^\p{L}\s-]+", RegexOptions.Compiled);

        private static readonly Regex SeveralNames = new(@"\p{L}\s*&\s*\p{L}|\p{L}\s+(?:and|or)\s+\p{L}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Joined = new(@"\p{L}\s*&\s*\p{L}|\p{L}\s+and\s+\p{L}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string Measured = @"(?:gallon|quart|pint|pound|cup|cupful|ounce|dozen|bushel|peck|barrel|gill|teacup|teacupful|bottle|jar)s?\b";

        private const string Timed = @"(?:\d+\s+)?(?:few|now|other|time|times|second|seconds|minute|minutes|hour|hours|day|days|week|weeks|morning|evening|night|side|sides|way|turn|addition)\b";

        private static readonly Regex Ratio = new(
            @"\b(?:to|for|in|into|on|onto|over|upon|inside)\s+every\b(?!\s+" + Timed + @")|\beach\b(?!\s+" + Timed + @")|\bper\b|\bin\s+the\s+proportion\b|\bhalf\s+and\s+half\b|\bhalf\b[^.;]*\band\s+half\b" +
            @"|\bto\s+(?:a|an|one|the|\d+)\s+" + Measured + @"|\bevery\s+(?:(?:\d+|a|an|one|two|three|four|six|twelve)\s+)?" + Measured +
            @"|(?<!\b(?:" + Counted + @")\s+)\bto\s+(?:" + Counted + @"|half\s+an?|three\s+quarters\s+of\s+an?)\s+(?:of\b|" + Measured + @")" +
            @"|\bfor\s+(?:half\s+an?|an?|" + Counted + @")\s+" + Measured + @"(?!\s+of\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string Counted = @"\d+(?:/\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve";

        private static readonly Regex SubPreparation = new(
            @"^(?:dissolved|dissolve|diluted|dilute|softened|soften|soaked|soak|steeped|steep)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Lead = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
            "half", "quarter", "third", "twenty", "thirty", "forty", "fifty", "dozen",
        };

        private static readonly HashSet<string> RangeJoin = new(StringComparer.OrdinalIgnoreCase) { "or", "to" };

        private static readonly HashSet<string> FractionParts = new(StringComparer.OrdinalIgnoreCase)
        {
            "half", "halves", "third", "thirds", "fourth", "fourths", "quarter", "quarters", "eighth", "eighths",
        };

        private static readonly HashSet<string> Measures = new(StringComparer.OrdinalIgnoreCase)
        {
            "cup", "cups", "cupful", "cupfuls", "spoonful", "spoonfuls", "tablespoon", "tablespoons", "tablespoonful", "tablespoonfuls",
            "teaspoon", "teaspoons", "teaspoonful", "teaspoonfuls", "dessertspoonful", "dessertspoonfuls", "ounce", "ounces", "oz",
            "pound", "pounds", "lb", "lbs", "pint", "pints", "quart", "quarts", "gill", "gills", "gallon", "gallons", "dozen",
            "handful", "handfuls", "pinch", "pinches", "slice", "slices", "clove", "cloves", "glass", "glasses", "wineglass", "wineglassful",
            "wineglasses", "wineglassfuls", "teacup", "teacups", "teacupful", "teacupfuls", "coffeecup", "coffeecupful", "tumbler", "tumblers",
            "tumblerful", "jill", "jills", "dessertspoon", "dessertspoons", "saltspoon", "saltspoonful", "saltspoonfuls",
        };

        private static readonly Dictionary<string, string> Temperatures = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cold"] = "cold", ["iced"] = "cold", ["ice-cold"] = "cold", ["chilled"] = "cold",
            ["warm"] = "warm", ["lukewarm"] = "warm", ["tepid"] = "warm",
            ["hot"] = "hot", ["boiling"] = "hot", ["scalding"] = "hot", ["seething"] = "hot",
        };

        private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
        {
            "of", "and", "a", "an", "large", "small", "medium", "heaping", "level", "scant", "full", "good", "generous", "fresh",
            "beaten", "well-beaten", "boiling", "cold", "hot", "warm", "melted", "sifted", "grated", "chopped",
        };

        private static readonly HashSet<string> Vessels = new(StringComparer.OrdinalIgnoreCase)
        {
            "kettle", "kettles", "kettleful", "pan", "pans", "panful", "saucepan", "saucepans", "pot", "pots", "potful", "basin", "basins",
            "bowl", "bowls", "dish", "dishes", "tub", "tubs", "pail", "pails", "bucket", "buckets", "barrel", "barrels", "boiler", "boilers",
            "bath", "baths", "jug", "jugs", "inch", "inches",
        };

        private static readonly HashSet<string> Liquids = new(StringComparer.OrdinalIgnoreCase)
        {
            "water", "milk", "cream", "stock", "broth", "vinegar", "wine", "juice", "oil", "brandy", "sherry", "rum", "whisky", "whiskey",
            "beer", "ale", "cider", "syrup", "molasses", "treacle", "liquor", "gravy", "soup", "buttermilk", "coffee", "tea", "honey",
        };

        private static bool IsNumberish(string w) =>
            Lead.Contains(w) || char.IsDigit(w[0]) || "½¼¾⅓⅔⅛".Contains(w[0])
            || (w.Contains('-') && w.Split('-').All(p => Lead.Contains(p) || FractionParts.Contains(p)));

        private static bool IsClean(string w) => IsNumberish(w) || Measures.Contains(w) || Filler.Contains(w) || FractionParts.Contains(w);

        private static bool IsVessel(string w) => Vessels.Contains(w) || Vessels.Contains(w.Split('-')[^1]);

        public static ParsedQuantity? Find(string? rawLine, IEnumerable<string> steps, IEnumerable<string>? otherLines = null, bool countedInPieces = true)
        {
            var written = (rawLine ?? string.Empty).Split(',', '(')[0];
            if (Joined.IsMatch(written)) return null;
            var alternatives = SeveralNames.IsMatch(written);
            var name = NonLetters.Replace(written, " ").Trim().ToLowerInvariant();
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 4) return null;

            var head = words[^1].TrimEnd('s');
            if (head.Length < 3) return null;
            var liquid = Liquids.Contains(head) || Liquids.Contains(words[^1]);

            var named = new Regex(@"(?<![\p{L}-])" + Regex.Escape(head) + @"(?:e?s)?(?![\p{L}-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if ((otherLines ?? Enumerable.Empty<string>()).Any(line => named.IsMatch(line ?? string.Empty))) return null;

            var ownWords = alternatives ? Array.Empty<string>() : words[..^1];
            var lead = alternatives ? string.Concat(words[..^1].Select(w => Regex.Escape(w) + @"\s+")) : string.Empty;
            var ownTemperatures = ownWords.Where(Temperatures.ContainsKey).Select(w => Temperatures[w]).ToHashSet();
            var found = new List<ParsedQuantity>();
            var alternative = false;
            var pattern = new Regex(@"(?<before>(?:[\p{L}\d/½¼¾⅓⅔⅛-]+\s+){1," + (7 + ownWords.Length) + @"})(?<food>" + lead + Regex.Escape(head) + @"(?:e?s)?)\b(?:(?=\s+(?<after>\p{L}+))|)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var clause in steps.SelectMany(s => Regex.Split(s ?? string.Empty, @"[.;!?]")).Where(sentence => !Ratio.IsMatch(sentence)).SelectMany(sentence => Regex.Split(sentence, @"[:,()]")))
            {
                foreach (Match m in pattern.Matches(clause))
                {
                    if (m.Groups["after"].Success && IsVessel(m.Groups["after"].Value)) continue;
                    if (head.Equals("water", StringComparison.OrdinalIgnoreCase) && m.Groups["food"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].Length > head.Length) continue;

                    var before = m.Groups["before"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var clean = before.Length;
                    for (var own = ownWords.Length; clean > 0 && own > 0 && before[clean - 1].Equals(ownWords[own - 1], StringComparison.OrdinalIgnoreCase); own--) clean--;
                    while (clean > 0 && IsClean(before[clean - 1]) && !IsVessel(before[clean - 1])) clean--;
                    var start = Array.FindIndex(before, clean, w => IsNumberish(w));
                    if (start < 0) continue;
                    if (start >= 2 && RangeJoin.Contains(before[start - 1]) && IsNumberish(before[start - 2])) start -= 2;
                    if (start >= 2 && before[start - 1] is "in" or "In" && SubPreparation.IsMatch(before[start - 2])) continue;

                    var phrase = before[start..];
                    var parsed = IngredientLineParser.Parse(string.Join(' ', phrase) + " " + m.Groups["food"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
                    if (parsed is not { Quantity: > 0 }) continue;
                    if (parsed.MeasurementName == IngredientLineParser.Piece && (liquid || !countedInPieces || phrase.Any(w => Measures.Contains(w) && !w.Equals("dozen", StringComparison.OrdinalIgnoreCase)))) continue;
                    var temperatures = phrase.Where(Temperatures.ContainsKey).Select(w => Temperatures[w]).ToHashSet();
                    if (ownTemperatures.Count > 0 && temperatures.Count > 0 && !ownTemperatures.Overlaps(temperatures)) continue;
                    if (Regex.IsMatch(clause, @"^\s*or\b", RegexOptions.IgnoreCase)) alternative = true;
                    found.Add(parsed);
                }
            }

            return !alternative && found.Distinct().Count() == 1 ? found[0] : null;
        }
    }
}
