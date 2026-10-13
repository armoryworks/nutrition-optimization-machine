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
    /// conflicting amounts give nothing. The number always comes from <see cref="IngredientLineParser"/>.
    /// </summary>
    public static class MethodAmountFinder
    {
        private static readonly Regex NonLetters = new(@"[^\p{L}\s-]+", RegexOptions.Compiled);

        private static readonly HashSet<string> Lead = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
            "half", "quarter", "third", "twenty", "thirty", "forty", "fifty", "dozen",
        };

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
        };

        private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
        {
            "of", "and", "a", "an", "large", "small", "medium", "heaping", "level", "scant", "full", "good", "generous", "fresh",
        };

        private static bool IsNumberish(string w) =>
            Lead.Contains(w) || char.IsDigit(w[0]) || "½¼¾⅓⅔⅛".Contains(w[0])
            || (w.Contains('-') && w.Split('-').All(p => Lead.Contains(p) || FractionParts.Contains(p)));

        private static bool IsClean(string w) => IsNumberish(w) || Measures.Contains(w) || Filler.Contains(w) || FractionParts.Contains(w);

        public static ParsedQuantity? Find(string? rawLine, IEnumerable<string> steps)
        {
            var name = NonLetters.Replace((rawLine ?? string.Empty).Split(',', '(')[0], " ").Trim().ToLowerInvariant();
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 4) return null;

            var head = words[^1].TrimEnd('s');
            if (head.Length < 3) return null;

            var found = new List<ParsedQuantity>();
            var pattern = new Regex(@"(?<before>(?:[\p{L}\d/½¼¾⅓⅔⅛-]+\s+){1,7})" + Regex.Escape(head) + @"(?:e?s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var clause in steps.SelectMany(s => Regex.Split(s ?? string.Empty, @"[.;:,!?()]")))
            {
                foreach (Match m in pattern.Matches(clause))
                {
                    var before = m.Groups["before"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var clean = before.Length;
                    while (clean > 0 && IsClean(before[clean - 1])) clean--;
                    var start = Array.FindIndex(before, clean, w => IsNumberish(w));
                    if (start < 0) continue;

                    var phrase = before[start..];
                    var parsed = IngredientLineParser.Parse(string.Join(' ', phrase) + " " + m.Value[m.Groups["before"].Length..]);
                    if (parsed is { Quantity: > 0 }) found.Add(parsed);
                }
            }

            return found.Distinct().Count() == 1 ? found[0] : null;
        }
    }
}
