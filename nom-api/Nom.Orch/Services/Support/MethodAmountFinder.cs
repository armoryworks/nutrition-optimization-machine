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
    /// half a pint", "for thirty pounds") anywhere in a sentence leaves every amount of that sentence alone ("To every quart allow
    /// ...; half a teaspoonful of mace", the sentence going on past ";" and the point of "lb."), even where it matches the
    /// recipe's own amount, and still counts as an amount of the food, so the part amount another sentence states fills
    /// nothing; "add to two quarts of" and "seven to eight cups of" are amounts, not proportions; a liquid another
    /// ingredient is dissolved, softened, soaked or steeped in gives nothing ("dissolved in one teaspoon of
    /// hot water"); a vessel or a length
    /// ("a kettle of water", "1-inch of water", "a water bath"), a utensil line ("tin plate", "tin cover": staging checkpoint 8, T1), the vessel a food is put
    /// into ("Put into a tumbler ... syrup"), washing water ("three or four waters"),
    /// an alternative ("..., or four fresh tomatoes"), a temperature the line contradicts ("cold
    /// vinegar" against "boiling vinegar"), an oven or fat temperature ("to 350C for frying") and a line that names
    /// what its food is for ("oil for frying", "fresh thyme for topping") give nothing either, and a count of pieces is never read for
    /// a liquid, from a measure ("a handful of parsley"), nor for a food the caller says is not counted
    /// in pieces, though a dozen is a count; one piece named in the singular with "a" ("on a corn tortilla"; "one egg" is one) is no amount for
    /// a line naming the food in the plural ("Corn tortillas"). A range ("six or eight pounds", "three to five gallons", "ten or a
    /// dozen") is read whole, however many of the food's words follow it, so it gives its lower end, as a
    /// line's range does. An ordinal fraction before the measure is read whole ("one-sixth of a pint", "three-quarters
    /// of a", "two thirds of a"); one standing where it cannot be read with its measure ("the sixth of a pint",
    /// "one-hundredth of a pint") gives nothing, never the measure alone, and so does a count of pieces with a
    /// fraction other than a half that is not a part of one of them ("a sixth of pepper", "one-fifth tomato pulp",
    /// "two-thirds potatoes", "three-quarters full of ice"), while "a quarter of an onion" is a quarter of one, the
    /// line's own words ("hind quarter of lamb") are no fraction, and "cut in quarters three onions" still counts
    /// three. A rib of meat is part of the food's name, not the stalk measure ("Have a rib roast of beef cut standing" for
    /// the line "rib roast of beef" is one piece of a food not counted in pieces, so nothing; staging checkpoint 7, S1). The number always comes from
    /// <see cref="IngredientLineParser"/>.
    /// </summary>
    public static class MethodAmountFinder
    {
        private static readonly Regex NonLetters = new(@"[^\p{L}\s-]+", RegexOptions.Compiled);

        private static readonly Regex SeveralNames = new(@"\p{L}\s*&\s*\p{L}|\p{L}\s+(?:and|or)\s+\p{L}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Joined = new(@"\p{L}\s*&\s*\p{L}|\p{L}\s+and\s+\p{L}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string Measured = @"(?:gallon|quart|pint|pound|cup|cupful|ounce|dozen|bushel|peck|barrel|gill|teacup|teacupful|bottle|jar)s?\b";

        private const string Timed = @"(?:\d+\s+)?(?:few|now|other|time|times|second|seconds|minute|minutes|hour|hours|day|days|week|weeks|morning|evening|night|side|sides|way|turn|addition)\b";

        private const string Placed = @"(?<!\b(?:add|adds|added|adding|pour|pours|poured|put|puts|return|returns|returned|turn|turns|turned|stir|stirs|stirred)\s+)";

        private const string NotRanged = @"(?<!\b(?:" + Counted + @")\s+)";

        private static readonly Regex Ratio = new(
            @"\b(?:to|for|in|into|on|onto|over|upon|inside)\s+every\b(?!\s+" + Timed + @")|\beach\b(?!\s+" + Timed + @")|\bper\b|\bin\s+the\s+proportion\b|\bhalf\s+and\s+half\b|\bhalf\b[^.;]*\band\s+half\b" +
            @"|" + Placed + NotRanged + @"\bto\s+(?:a|an|one|the|\d+)\s+" + Measured + @"|\bevery\s+(?:(?:\d+|a|an|one|two|three|four|six|twelve)\s+)?" + Measured +
            @"|" + Placed + NotRanged + @"\bto\s+(?:(?:" + Counted + @")(?:\s+(?:or|to)\s+(?:" + Counted + @"))?|half\s+an?|three\s+quarters\s+of\s+an?)\s+(?:of\b|" + Measured + @")" +
            @"|\bfor\s+(?:half\s+an?|an?|" + Counted + @")\s+" + Measured + @"(?!\s+of\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string Counted = @"\d+(?:/\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty|thirty|forty|fifty|sixty|hundred|a\s+hundred|one\s+hundred";

        private static readonly Regex SentenceEnd = new(
            @"(?<!\b(?:lb|lbs|oz|ozs|pt|pts|qt|qts|doz|tsp|tsps|tbsp|tbsps|gal|teaspn|tablespn))\.(?!\d)|[!?]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Purpose = new(@"\s(?:for|to)\s+\p{L}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Degrees = new(@"^\d{2,3}°?[cf]$|^degrees?$|°", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            "half", "halves", "third", "thirds", "fourth", "fourths", "quarter", "quarters", "fifth", "fifths", "sixth", "sixths",
            "seventh", "sevenths", "eighth", "eighths", "ninth", "ninths", "tenth", "tenths", "twelfth", "twelfths", "sixteenth", "sixteenths",
        };

        private static readonly Regex Ordinal = new(@"^(?:four|fif|six|seven|eigh|nin|ten|eleven|twelf|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twentie|thirtie|fortie|fiftie|hundred|thousand)ths?$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

        private static readonly HashSet<string> Utensils = new(StringComparer.OrdinalIgnoreCase)
        {
            "plate", "plates", "platter", "platters", "mould", "moulds", "mold", "molds", "tin", "tins", "skillet", "skillets", "griddle",
            "spider", "sheet", "sheets", "tumbler", "tumblers", "glass", "glasses", "cloth", "sieve", "colander", "cover", "covers", "lid", "lids",
        };

        private static readonly HashSet<string> Into = new(StringComparer.OrdinalIgnoreCase) { "in", "into", "inside" };

        private static readonly HashSet<string> Liquids = new(StringComparer.OrdinalIgnoreCase)
        {
            "water", "milk", "cream", "stock", "broth", "vinegar", "wine", "juice", "oil", "brandy", "sherry", "rum", "whisky", "whiskey",
            "beer", "ale", "cider", "syrup", "molasses", "treacle", "liquor", "gravy", "soup", "buttermilk", "coffee", "tea", "honey",
        };

        private static bool IsNumberish(string w) =>
            Lead.Contains(w) || char.IsDigit(w[0]) || "½¼¾⅓⅔⅛".Contains(w[0])
            || (w.Contains('-') && w.Split('-').All(p => Lead.Contains(p) || FractionParts.Contains(p)));

        private static bool IsClean(string w) => IsNumberish(w) || Measures.Contains(w) || Filler.Contains(w) || FractionParts.Contains(w);

        private static bool Unread(string[] before, int clean, int start)
        {
            for (var i = clean; i < start; i++)
            {
                if (FractionParts.Contains(before[i]) && !IsNumberish(before[i + 1])) return true;
            }
            if (clean == 0) return false;
            var word = before[clean - 1];
            var parts = word.Split('-');
            return (parts.Length > 1 && parts[0] is not ("a" or "an" or "A" or "An") && Lead.Contains(parts[0]) && Ordinal.IsMatch(parts[^1]))
                || (clean >= 2 && Ordinal.IsMatch(word) && Lead.Contains(before[clean - 2]) && before[clean - 2] is not ("a" or "an" or "A" or "An"));
        }

        private static bool Fractional(string w) =>
            w.Split('-').Any(p => FractionParts.Contains(p) && !p.Equals("half", StringComparison.OrdinalIgnoreCase) && !p.Equals("halves", StringComparison.OrdinalIgnoreCase));

        private static bool PartOfOne(string[] words)
        {
            for (var j = 0; j < words.Length; j++)
            {
                if (!Fractional(words[j])) continue;
                if (j + 2 >= words.Length || !words[j + 1].Equals("of", StringComparison.OrdinalIgnoreCase) || words[j + 2] is not ("a" or "an" or "A" or "An")) return false;
            }
            return true;
        }

        private static bool IsVessel(string w) => Vessels.Contains(w) || Vessels.Contains(w.Split('-')[^1]);

        public static ParsedQuantity? Find(string? rawLine, IEnumerable<string> steps, IEnumerable<string>? otherLines = null, bool countedInPieces = true)
        {
            var written = (rawLine ?? string.Empty).Split(',', '(')[0];
            if (Joined.IsMatch(written) || Purpose.IsMatch(written)) return null;
            var alternatives = SeveralNames.IsMatch(written);
            var name = NonLetters.Replace(written, " ").Trim().ToLowerInvariant();
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 4) return null;

            if (IsVessel(words[^1]) || Utensils.Contains(words[^1])) return null;
            var head = words[^1].TrimEnd('s');
            if (head.Length < 3) return null;
            var liquid = Liquids.Contains(head) || Liquids.Contains(words[^1]);
            var plural = words[^1].Length > head.Length && !words[^1].EndsWith("ss", StringComparison.OrdinalIgnoreCase);

            var named = new Regex(@"(?<![\p{L}-])" + Regex.Escape(head) + @"(?:e?s)?(?![\p{L}-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if ((otherLines ?? Enumerable.Empty<string>()).Any(line => named.IsMatch(line ?? string.Empty))) return null;

            var ownWords = alternatives ? Array.Empty<string>() : words[..^1];
            var lead = alternatives ? string.Concat(words[..^1].Select(w => Regex.Escape(w) + @"\s+")) : string.Empty;
            var ownTemperatures = ownWords.Where(Temperatures.ContainsKey).Select(w => Temperatures[w]).ToHashSet();
            var found = new List<ParsedQuantity>();
            var alternative = false;
            var proportioned = false;
            var unread = false;
            var pattern = new Regex(@"(?<before>(?:[\p{L}\d/½¼¾⅓⅔⅛-]+\s+){1," + (7 + ownWords.Length) + @"})(?<food>" + lead + Regex.Escape(head) + @"(?:e?s)?)\b(?:(?=\s+(?<after>\p{L}+))|)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var (clause, ratio) in steps.SelectMany(s => SentenceEnd.Split(s ?? string.Empty))
                .SelectMany(sentence => Regex.Split(sentence, @"[;:,()]").Select(clause => (clause, Ratio.IsMatch(sentence)))))
            {
                foreach (Match m in pattern.Matches(clause))
                {
                    if (m.Groups["after"].Success && IsVessel(m.Groups["after"].Value)) continue;
                    if (head.Equals("water", StringComparison.OrdinalIgnoreCase) && m.Groups["food"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].Length > head.Length) continue;

                    var before = m.Groups["before"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var clean = before.Length;
                    for (var own = ownWords.Length; clean > 0 && own > 0 && before[clean - 1].Equals(ownWords[own - 1], StringComparison.OrdinalIgnoreCase); own--) clean--;
                    var ownStart = clean;
                    while (clean > 0 && IsClean(before[clean - 1]) && !IsVessel(before[clean - 1])) clean--;
                    var start = Array.FindIndex(before, clean, w => IsNumberish(w));
                    if (start < 0) continue;
                    if (Unread(before, clean, start))
                    {
                        unread = true;
                        continue;
                    }
                    if (start >= 2 && RangeJoin.Contains(before[start - 1]) && IsNumberish(before[start - 2])) start -= 2;
                    if (start >= 2 && before[start - 1] is "in" or "In" && SubPreparation.IsMatch(before[start - 2])) continue;

                    var phrase = before[start..];
                    if (phrase.Any(Degrees.IsMatch)) continue;
                    if (start >= 1 && Into.Contains(before[start - 1]) && !phrase.Contains("of", StringComparer.OrdinalIgnoreCase) && phrase.Any(Measures.Contains)) continue;
                    var parsed = IngredientLineParser.Parse(string.Join(' ', phrase) + " " + m.Groups["food"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
                    if (parsed is not { Quantity: > 0 })
                    {
                        if (phrase.Any(Fractional)) unread = true;
                        continue;
                    }
                    if (parsed.MeasurementName == IngredientLineParser.Piece && !PartOfOne(before[start..Math.Max(start, ownStart)]))
                    {
                        unread = true;
                        continue;
                    }
                    if (parsed.MeasurementName == IngredientLineParser.Piece && (liquid || !countedInPieces || phrase.Any(w => Measures.Contains(w) && !w.Equals("dozen", StringComparison.OrdinalIgnoreCase)))) continue;
                    if (parsed.MeasurementName == IngredientLineParser.Piece && parsed.Quantity <= 1 && plural && phrase[0].ToLowerInvariant() is "a" or "an"
                        && m.Groups["food"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].Length < words[^1].Length)
                    {
                        unread = true;
                        continue;
                    }
                    var temperatures = phrase.Where(Temperatures.ContainsKey).Select(w => Temperatures[w]).ToHashSet();
                    if (ownTemperatures.Count > 0 && temperatures.Count > 0 && !ownTemperatures.Overlaps(temperatures)) continue;
                    if (Regex.IsMatch(clause, @"^\s*or\b", RegexOptions.IgnoreCase)) alternative = true;
                    if (ratio) proportioned = true;
                    else found.Add(parsed);
                }
            }

            return !alternative && !proportioned && !unread && found.Distinct().Count() == 1 ? found[0] : null;
        }
    }
}
