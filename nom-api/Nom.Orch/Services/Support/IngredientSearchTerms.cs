using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Splits an ingredient picker query into word stems that must all appear in a food's name,
    /// in any order, so "black beans" finds USDA's "Beans, black, mature seeds, canned" and
    /// "canned black" finds it too. A stem is always a prefix of the typed word, so a stem
    /// matches everything the word itself would; trailing plurals are dropped ("tomatoes" →
    /// "tomato", "berries" → "berr") so singular catalog names match plural queries.
    /// </summary>
    public static class IngredientSearchTerms
    {
        /// <summary>Most stems a single query contributes; extra words are ignored.</summary>
        public const int MaxStems = 6;

        private static readonly Regex WordSeparators = new(
            @"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
        {
            "a", "an", "and", "in", "of", "or", "the", "with"
        };

        /// <summary>Distinct lower-case stems of the query's words, in the order typed.</summary>
        public static IReadOnlyList<string> Stems(string? query) =>
            WordSeparators.Split((query ?? string.Empty).ToLowerInvariant())
                .Where(w => w.Length >= 2 || (w.Length == 1 && char.IsDigit(w[0])))
                .Where(w => !StopWords.Contains(w))
                .Select(Stem)
                .Distinct()
                .Take(MaxStems)
                .ToList();

        /// <summary>The word without a trailing plural ending; stemming never cuts a word below three letters.</summary>
        public static string Stem(string word)
        {
            if (word.Length > 5 && word.EndsWith("ies", StringComparison.Ordinal)) return word[..^3];
            if (word.Length > 4 && (word.EndsWith("oes", StringComparison.Ordinal) ||
                                    word.EndsWith("ches", StringComparison.Ordinal) ||
                                    word.EndsWith("shes", StringComparison.Ordinal) ||
                                    word.EndsWith("sses", StringComparison.Ordinal) ||
                                    word.EndsWith("xes", StringComparison.Ordinal))) return word[..^2];
            if (word.Length > 3 && word.EndsWith('s') &&
                !word.EndsWith("ss", StringComparison.Ordinal) &&
                !word.EndsWith("us", StringComparison.Ordinal) &&
                !word.EndsWith("is", StringComparison.Ordinal)) return word[..^1];
            return word;
        }
    }
}
