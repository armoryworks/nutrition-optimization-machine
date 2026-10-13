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
    /// comes from <see cref="IngredientLineParser"/> reading that quote — never from the model.
    /// </summary>
    public static class RecipeRepairGrounding
    {
        public const int MinStepLength = 10;

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

        private static readonly Regex NumberToken = new(@"^[\d¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞./,\-–]+$", RegexOptions.Compiled);
        private static readonly Regex Words = new(@"[\p{L}\d¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞./\-–]+", RegexOptions.Compiled);

        public static string Normalize(string? text) =>
            Regex.Replace(System.Net.WebUtility.HtmlDecode(text ?? string.Empty), @"\s+", " ").Trim();

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

        public static ParsedQuantity? GroundQuantity(string rawLine, string? ingredientName, string? quote, IEnumerable<string> sourceTexts)
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

            return parsed;
        }

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
