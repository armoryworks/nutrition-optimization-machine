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
        public const string UnreadMeasureRefusal = "names a measure that is not a unit";
        public const string ArticleRefusal = "counts \"a\" or \"an\" on a food with no weight per item";

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
            @"\b(?:\p{L}*spoon(?:ful)?s?|\p{L}*glass(?:ful)?(?:e?s)?|\p{L}*cupfuls?|handfuls?|bunch(?:es)?|sprinkl(?:e|ing)s?|pinch(?:es)?|dash(?:es)?|scruples?|drachms?|drams?|quantit(?:y|ies)|ladle(?:ful)?s?|knobs?|lumps?|bits?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        /// Why a quote read as a count in pieces states no count, or null when it does. A quote that
        /// names a measure the parser did not read as a unit ("a glassful of wine", "one scruple of
        /// ginger") is refused, and so is one whose only number is "a" or "an" ("a clean tin"),
        /// unless the food has a weight per item ("an egg").
        /// </summary>
        public static string? PieceRefusal(string? quote, decimal? gramsPerItem)
        {
            var text = Normalize(quote);
            if (UnreadMeasure.IsMatch(text)) return UnreadMeasureRefusal;

            var tokens = Words.Matches(text).Select(m => m.Value).ToList();
            var named = tokens.FindIndex(IsContentWord);
            var lead = named > 0 ? tokens.Take(named) : tokens;
            return lead.Any(IsCount) || gramsPerItem > 0 ? null : ArticleRefusal;
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
