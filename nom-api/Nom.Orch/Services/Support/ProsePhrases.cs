using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The words and the five-word phrases the prose check and the stock-phrase index compare.
    /// Text is prepared as <see cref="ProseQuantities.Prepare"/> does, lower-cased, apostrophes are
    /// dropped, and every word holding a digit or a fraction becomes <c>#</c>, so "Bake for 25
    /// minutes" and "bake for 30 minutes" share their phrases. A phrase is kept as a 64-bit FNV-1a
    /// hash of its words joined by single spaces, which is the same in every process.
    /// </summary>
    public static class ProsePhrases
    {
        public const int Length = 5;
        public const string Number = "#";

        private static readonly Regex WordPattern = new(@"[\p{L}\p{N}¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Apostrophes = new(@"['’]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex HasDigit = new(@"[\p{N}¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The words of <paramref name="text"/> in the form the phrases use.</summary>
        public static List<string> Words(string? text) => WordsOfPrepared(ProseQuantities.Prepare(text));

        /// <summary>The words of a text <see cref="ProseQuantities.Prepare"/> has already prepared.</summary>
        public static List<string> WordsOfPrepared(string prepared) =>
            WordPattern.Matches(Apostrophes.Replace(prepared.ToLowerInvariant(), string.Empty))
                .Select(m => HasDigit.IsMatch(m.Value) ? Number : m.Value)
                .ToList();

        /// <summary>The hash of each five-word phrase of <paramref name="words"/>, in order, one per starting word.</summary>
        public static List<long> Hashes(IReadOnlyList<string> words)
        {
            var hashes = new List<long>(Math.Max(0, words.Count - Length + 1));
            for (var i = 0; i + Length <= words.Count; i++) hashes.Add(Hash(words, i));
            return hashes;
        }

        /// <summary>The hash of the phrase a person would write as text ("preheat the oven to 350"), read as <see cref="Words"/> reads it.</summary>
        public static long Hash(string phrase)
        {
            var words = Words(phrase);
            return Hash(words, 0, words.Count);
        }

        public static long Hash(IReadOnlyList<string> words, int start, int count = Length)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            for (var i = start; i < start + count; i++)
            {
                if (i > start)
                {
                    hash ^= ' ';
                    hash *= prime;
                }

                foreach (var b in Encoding.UTF8.GetBytes(words[i]))
                {
                    hash ^= b;
                    hash *= prime;
                }
            }

            return unchecked((long)hash);
        }
    }

    /// <summary>
    /// The stock phrases of recipe prose: five-word phrases that at least <see cref="StockSites"/>
    /// recipe sites use, so a phrase is stock when two or more sites besides the one a source came
    /// from use it ("preheat the oven to #", "bring a large pot of"). Built by
    /// <c>ProseStockService</c> from the stored source documents; <see cref="Version"/> is the
    /// index build it came from, and <see cref="Empty"/> (version 0) treats every phrase as
    /// distinctive, which is the strict reading.
    /// </summary>
    public sealed class ProseStockIndex
    {
        public const int StockSites = 3;

        private readonly HashSet<long> _stock;

        public ProseStockIndex(int version, IEnumerable<long> stock)
        {
            Version = version;
            _stock = stock.ToHashSet();
        }

        public static ProseStockIndex Empty { get; } = new(0, Array.Empty<long>());

        public int Version { get; }

        public int Count => _stock.Count;

        public bool IsStock(long hash) => _stock.Contains(hash);
    }
}
