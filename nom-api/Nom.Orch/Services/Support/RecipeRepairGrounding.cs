using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Deterministic checks that keep model-proposed recipe repairs honest. A step split is accepted
    /// only when the steps, joined, are the original text (whitespace aside). A quantity is accepted
    /// only when the model's quote is verbatim source text that names the ingredient, and the amount
    /// comes from <see cref="IngredientLineParser"/> reading that quote — never from the model. A count
    /// in pieces also needs a stated number, or a food that has a weight per item.
    /// </summary>
    public static class RecipeRepairGrounding
    {
        public const int MinStepLength = 10;
        public const string UnreadMeasureRefusal = "unread-measure";
        public const string VagueAmountRefusal = "vague-amount";
        public const string NotAnItemRefusal = "not-an-item";
        public const string NoCountRefusal = "no-count";

        public const string StatesCount = "states-a-count";
        public const string WeightPerItem = "weight-per-item";
        public const string OneCountableItem = "one-countable-item";

        /// <summary>The reasons <see cref="PieceRefusal"/> gives, each with what it means.</summary>
        public static readonly IReadOnlyDictionary<string, string> RefusalReasons = new Dictionary<string, string>
        {
            [UnreadMeasureRefusal] = "names a measure that is not a unit (\"a glassful of wine\", \"a bunch of parsley\")",
            [VagueAmountRefusal] = "\"a\" or \"an\" before a vague amount (\"a little butter\", \"a few cloves\", \"a couple of pigeons\")",
            [NotAnItemRefusal] = "\"a\" or \"an\" before a preparation or a utensil, not a countable item (\"a white sauce\", \"a clean tin\")",
            [NoCountRefusal] = "states no number and starts with no \"a\" or \"an\" (\"the onion\", \"some salt\")",
        };

        /// <summary>Why <see cref="PieceAcceptance"/> leaves a count in pieces alone, each with what it means.</summary>
        public static readonly IReadOnlyDictionary<string, string> AcceptanceReasons = new Dictionary<string, string>
        {
            [StatesCount] = "states a number (\"six neck chops\", \"½ lemon\")",
            [WeightPerItem] = "the food has a weight per item (\"an egg\")",
            [OneCountableItem] = "\"a\" or \"an\" before one countable item (\"a bay leaf\", \"a leg of mutton\", \"a sandwich loaf\")",
        };

        private static readonly HashSet<string> QuantityWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "of", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "half", "one-half", "quarter", "one-quarter", "three-quarters", "quarters",
            "third", "thirds", "one-third", "two-thirds", "large", "medium", "small", "whole",
        };

        private static readonly HashSet<string> NotIngredientWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "the", "for", "with", "into", "from", "some", "little", "few", "taste", "needed", "about", "fresh",
            "chopped", "minced", "sliced", "diced", "grated", "beaten", "melted", "large", "medium", "small", "whole",
            "pound", "pounds", "ounce", "ounces", "cup", "cups", "pint", "pints", "quart", "quarts", "gill", "gills",
            "teaspoon", "teaspoons", "teaspoonful", "teaspoonfuls", "tablespoon", "tablespoons", "tablespoonful",
            "tablespoonfuls", "spoonful", "spoonfuls", "pinch", "dash", "half", "quarter", "one", "two", "three",
            "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "dozen", "piece", "pieces",
        };

        private static readonly HashSet<string> CountWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "dozen",
            "half", "one-half", "quarter", "one-quarter", "three-quarters", "quarters", "third", "thirds", "one-third", "two-thirds",
        };

        private static readonly Regex UnreadMeasure = new(
            @"\b(?:\p{L}*spoon(?:ful)?s?|\p{L}*glass(?:ful)?(?:e?s)?|\p{L}*cupfuls?|handfuls?|bunch(?:es)?|sprinkl(?:e|ing)s?|pinch(?:es)?|dash(?:es)?|scruples?|drachms?|drams?|quantit(?:y|ies)|ladle(?:ful)?s?|knobs?|lumps?|bits?|\p{L}+cups?|\p{L}+full?s?|thimbles?|jugs?|pecks?|pints?|quarts?|gills?|jills?|bushels?|gallons?|stones?|tbsps?|tbps?|tsps?|bouquets?|faggots?|bundles?|measures?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LeadSegment = new(
            @"^.*?(?=\s+(?:of|with|or|for|to|into|in|from|made|and|the)\b|\s*[,;:(]|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex MeasureSegment = new(
            @"^.*?(?=\s+(?:with|or)\b|\s*\(|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex HeadSegment = new(
            @"^.*?(?=\s+of\b|\s*[,;:(]|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { "a", "an" };

        private static readonly HashSet<string> VagueWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "little", "few", "couple", "some", "suspicion", "shaving", "shavings", "grain", "grains", "touch", "trifle",
            "morsel", "drop", "drops", "sprinkling", "squeeze", "splash", "hint", "portion", "part", "equal", "enough",
            "plenty", "sufficient", "layer", "coating", "amount", "proportion", "weight", "dust", "dusting", "variety",
            "batch", "brace", "pair",
        };

        private static readonly HashSet<string> NotItemWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "sauce", "gravy", "forcemeat", "stuffing", "dressing", "puree", "purée", "paste", "crust", "mixture", "batter",
            "stock", "broth", "bouillon", "infusion", "decoction", "syrup", "glaze", "ragout", "ragoût", "hash", "marinade",
            "liquor", "essence", "seasoning", "icing", "frosting", "custard", "mayonnaise", "salad", "soup", "jelly",
            "fricassee", "fricassée", "brine", "marmalade", "jam", "preparation", "bavarois", "butter", "iceing",
            "cloth", "cloths", "pan", "saucepan", "stewpan", "basin", "bowl", "dish", "paper", "bag", "mould", "mold",
            "cutter", "needle", "thread", "string", "twine", "saucer", "barrel", "brick", "tin", "sieve", "towel", "napkin",
            "skewer", "knife", "fork", "board", "plate", "pot", "kettle", "oven", "toothpick", "tips", "spoon", "jar", "box",
            "tub", "pail", "funnel", "muslin", "brush", "vessel", "bottle", "handkerchief", "tube", "basket", "cup", "beater",
        };

        private static readonly Regex NumberToken = new(@"^[\d¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞./,\-–]+$", RegexOptions.Compiled);
        private static readonly Regex SentenceEnd = new(@"(?<=[.;!?][""'”’)]?)\s+(?=[""'“‘(]?\p{Lu})", RegexOptions.Compiled);
        private static readonly Regex Words = new(@"[\p{L}\d¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞./\-–]+", RegexOptions.Compiled);

        public static string Normalize(string? text) =>
            Regex.Replace(System.Net.WebUtility.HtmlDecode(text ?? string.Empty), @"\s+", " ").Trim();

        /// <summary>
        /// The method divided at sentence ends (a full stop, semicolon, question or exclamation mark followed by
        /// a capital), passed through <see cref="AcceptSplit"/>; null when it is a single sentence.
        /// </summary>
        public static List<string>? SplitSentences(string? method)
        {
            var text = Normalize(method);
            if (text.Length == 0) return null;
            return AcceptSplit(text, SentenceEnd.Split(text));
        }

        public static List<string>? AcceptSplit(string original, IReadOnlyList<string>? proposed)
        {
            if (proposed == null || proposed.Count < 2 || proposed.Any(string.IsNullOrWhiteSpace)) return null;
            if (!string.Equals(Normalize(string.Join(" ", proposed)), Normalize(original), StringComparison.Ordinal)) return null;

            var steps = new List<string>();
            foreach (var step in proposed.Select(Normalize))
            {
                if (steps.Count > 0 && (step.Length < MinStepLength || steps[^1].Length < MinStepLength))
                {
                    steps[^1] = steps[^1] + " " + step;
                }
                else
                {
                    steps.Add(step);
                }
            }
            return steps.Count >= 2 ? steps : null;
        }

        public static ParsedQuantity? GroundQuantity(string rawLine, string? ingredientName, string? quote, IEnumerable<string> sourceTexts, decimal? gramsPerItem = null)
        {
            var normalizedQuote = Normalize(quote).TrimEnd('.', ',', ';', ':');
            if (normalizedQuote.Length == 0) return null;

            if (!sourceTexts.Prepend(rawLine).Select(Normalize).Any(s => s.Contains(normalizedQuote, StringComparison.OrdinalIgnoreCase))) return null;

            var parsed = IngredientLineParser.Parse(normalizedQuote);
            if (parsed == null) return null;

            var ingredientStems = ContentWords(rawLine).Concat(ContentWords(ingredientName)).Select(Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tokens = Words.Matches(normalizedQuote).Select(m => m.Value).ToList();
            var named = tokens.FindIndex(IsContentWord);
            if (named < 0 || !ingredientStems.Contains(Stem(tokens[named]))) return null;

            if (parsed.MeasurementName == IngredientLineParser.Piece
                && !tokens.Take(named).All(t => NumberToken.IsMatch(t) || QuantityWords.Contains(t)))
            {
                return null;
            }

            if (parsed.MeasurementName == IngredientLineParser.Piece && PieceRefusal(normalizedQuote, gramsPerItem) != null) return null;

            return parsed;
        }

        /// <summary>
        /// Why a quote read as a count in pieces states no count, or null when it does. A measure is
        /// read only before the first "with" or "or", so one that belongs to another food ("1 large egg
        /// beaten with 1 Tablespoon milk") does not count against this one, and a number only before
        /// the first "of", "with", "or", comma and the like. A measure the parser did not read as a unit ("a glassful of good red
        /// wine", "one scruple of ginger") is refused. "A" or "an" counts one item ("a bay leaf", "a leg
        /// of mutton"), except before a vague amount ("a little butter", "a few", "a couple of") or a
        /// preparation or utensil ("a white sauce", "a clean tin"). A quote with neither a number nor an
        /// article is refused unless the food has a weight per item.
        /// </summary>
        public static string? PieceRefusal(string? quote, decimal? gramsPerItem) =>
            Judge(quote, gramsPerItem) is { Refused: true } verdict ? verdict.Reason : null;

        /// <summary>Why a count in pieces read from the quote stands, or null when <see cref="PieceRefusal"/> refuses it.</summary>
        public static string? PieceAcceptance(string? quote, decimal? gramsPerItem) =>
            Judge(quote, gramsPerItem) is { Refused: false } verdict ? verdict.Reason : null;

        private static (bool Refused, string Reason) Judge(string? quote, decimal? gramsPerItem)
        {
            var text = Normalize(quote).TrimEnd('.', ',', ';', ':');
            var lead = LeadSegment.Match(text).Value;
            if (UnreadMeasure.IsMatch(MeasureSegment.Match(text).Value)) return (true, UnreadMeasureRefusal);

            var tokens = Words.Matches(lead).Select(m => m.Value).ToList();
            if (tokens.Any(IsCount)) return (false, StatesCount);
            if (gramsPerItem > 0) return (false, WeightPerItem);
            if (tokens.Count > 0 && Articles.Contains(tokens[0]))
            {
                if (tokens.Any(VagueWords.Contains)) return (true, VagueAmountRefusal);
                var head = Words.Matches(HeadSegment.Match(text).Value).SelectMany(m => m.Value.Split('-')).ToList();
                return head.Any(NotItemWords.Contains) ? (true, NotAnItemRefusal) : (false, OneCountableItem);
            }

            var all = Words.Matches(text).Select(m => m.Value).ToList();
            var named = all.FindIndex(IsContentWord);
            return (named > 0 ? all.Take(named) : all).Any(IsCount) ? (false, StatesCount) : (true, NoCountRefusal);
        }

        private static bool IsCount(string token) =>
            CountWords.Contains(token) || (NumberToken.IsMatch(token) && token.Any(c => char.IsDigit(c) || !char.IsAscii(c)));

        private static IEnumerable<string> ContentWords(string? text) =>
            Words.Matches(text ?? string.Empty).Select(m => m.Value).Where(IsContentWord);

        private static bool IsContentWord(string word) =>
            word.Length >= 3 && word.All(char.IsLetter) && !NotIngredientWords.Contains(word);

        private static string Stem(string word)
        {
            var w = word.ToLowerInvariant();
            if (w.EndsWith("ies") && w.Length > 4) return w[..^3] + "y";
            if (w.EndsWith("oes") && w.Length > 4) return w[..^2];
            if (w.EndsWith("es") && w.Length > 4 && (w.EndsWith("ches") || w.EndsWith("shes") || w.EndsWith("sses") || w.EndsWith("xes"))) return w[..^2];
            if (w.EndsWith("s") && !w.EndsWith("ss") && w.Length > 3) return w[..^1];
            return w;
        }
    }
}
