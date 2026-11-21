using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nom.Orch.Services.Support
{
    /// <summary>One sized container line of an old or undated book, as the corpus tiers read it: its book, era, food, container kind, size word and the grams of one container it states.</summary>
    public sealed record CorpusLine(string Book, string Era, long IngredientId, string Kind, string SizeWord, decimal Grams, string Ref = "");

    /// <summary>
    /// A size the recipe corpus gives one food, container kind and size word: within one book
    /// (<see cref="ContainerCorpusSizes.Book"/>, scope the book) or across the books of one era
    /// (<see cref="ContainerCorpusSizes.Era"/>, scope the era), with its evidence: the sizes stated
    /// and how often, the spread, and why there is none when the mode is not clear.
    /// </summary>
    public sealed record CorpusSize(
        string Tier,
        string Scope,
        long IngredientId,
        string Kind,
        string SizeWord,
        int Grams,
        int Lines,
        int Books,
        int Band,
        int Votes,
        int RunnerUp,
        decimal Min,
        decimal Max,
        string Sizes,
        string? Refusal,
        int BandBooks = 0)
    {
        public bool Passes => Refusal == null;

        /// <summary>The largest share by which a size the corpus states differs from the one taken: the worst-case error of the size, from its spread.</summary>
        public decimal WorstCase => Grams <= 0 ? 0m : Math.Round(Math.Max(Grams - Min, Max - Grams) / Grams, 3);
    }

    /// <summary>
    /// Sizes for unsized containers in old or undated books, derived from the sized lines of the
    /// recipe corpus itself, before any cited period source or today's size: first the sized lines
    /// of the same food and container in the same book ("1 3-oz. package cream cheese" there gives
    /// the size of "1 package cream cheese" there), then those of the books of the same era
    /// (recipe.RecipeSourceEra). A book's lines give a size when one size (±5 %) holds more than
    /// half of them and at least twice the next; an era gives one when, counting each book once
    /// per size it states, at least two different books state a size within the mode's band, it
    /// holds at least half of the votes and twice the next. Every size keeps its evidence (the sizes stated and how often, the
    /// spread), and the same lines always give the same sizes, so ops/container-era-facts.py
    /// reproduces them from a capture of the corpus.
    /// </summary>
    public static class ContainerCorpusSizes
    {
        public const string Version = "container-corpus/2";
        public const string BookTier = "book";
        public const string EraTier = "era";
        public const string BookSourcePrefix = "deterministic:corpus-book:";
        public const string EraSourcePrefix = "deterministic:corpus-era:";
        public const int MinimumBooks = 2;
        public const string FewBooks = "fewer than two books";

        /// <summary>The corpus tier a size row came from (<see cref="BookTier"/> or <see cref="EraTier"/>), or null for any other source.</summary>
        public static string? TierOf(string? sourceRef) =>
            sourceRef == null ? null
            : sourceRef.StartsWith(BookSourcePrefix, StringComparison.Ordinal) ? BookTier
            : sourceRef.StartsWith(EraSourcePrefix, StringComparison.Ordinal) ? EraTier
            : null;

        /// <summary>The sized line a corpus line is mined from: an old or undated book's line that names a container and states its size in grams (or a volume the food has a density for); null for any other line.</summary>
        public static CorpusLine? Read(string? book, string era, long ingredientId, string? rawLine, decimal? gramsPerMilliliter, string reference = "")
        {
            if (string.IsNullOrWhiteSpace(book) || era is not (ContainerEras.Historic or ContainerEras.Unknown)) return null;
            if (ContainerKinds.FromText(rawLine) is not { } kind || IngredientLineParser.CanNumber(rawLine) != null) return null;
            if (ContainerSizeResolver.StatedPerContainer(rawLine, gramsPerMilliliter) is not { Reason: null, Grams: > 0 } stated) return null;
            return new CorpusLine(book, era, ingredientId, kind, ContainerKinds.SizeWord(rawLine), stated.Grams, reference);
        }

        /// <summary>The size each book's own lines give each food, kind and size word, refused ones included.</summary>
        public static IReadOnlyList<CorpusSize> Book(IEnumerable<CorpusLine> lines) =>
            lines.GroupBy(l => (l.Book, l.IngredientId, l.Kind, l.SizeWord))
                .OrderBy(g => g.Key.Book, StringComparer.Ordinal).ThenBy(g => g.Key.IngredientId).ThenBy(g => g.Key.Kind, StringComparer.Ordinal).ThenBy(g => g.Key.SizeWord, StringComparer.Ordinal)
                .Select(g => BookSize(g.Key.Book, g.ToList())!)
                .ToList();

        /// <summary>The size each era's books give each food, kind and size word, refused ones included.</summary>
        public static IReadOnlyList<CorpusSize> Era(IEnumerable<CorpusLine> lines) =>
            lines.GroupBy(l => (l.Era, l.IngredientId, l.Kind, l.SizeWord))
                .OrderBy(g => g.Key.Era, StringComparer.Ordinal).ThenBy(g => g.Key.IngredientId).ThenBy(g => g.Key.Kind, StringComparer.Ordinal).ThenBy(g => g.Key.SizeWord, StringComparer.Ordinal)
                .Select(g => EraSize(g.Key.Era, g.ToList())!)
                .ToList();

        /// <summary>The size one book's lines of one key give; null when there are none.</summary>
        public static CorpusSize? BookSize(string book, IReadOnlyList<CorpusLine> lines)
        {
            if (lines.Count == 0) return null;
            var votes = lines.GroupBy(l => Gram(l.Grams)).ToDictionary(g => g.Key, g => g.Count());
            return Size(BookTier, book, lines, votes, 1);
        }

        /// <summary>The size the books of one era give one key, each book counted once per size it states; null when there are no lines.</summary>
        public static CorpusSize? EraSize(string era, IReadOnlyList<CorpusLine> lines)
        {
            if (lines.Count == 0) return null;
            var votes = lines.Select(l => (l.Book, Gram: Gram(l.Grams))).Distinct().GroupBy(v => v.Gram).ToDictionary(g => g.Key, g => g.Count());
            return Size(EraTier, era, lines, votes, MinimumBooks);
        }

        private static CorpusSize Size(string tier, string scope, IReadOnlyList<CorpusLine> lines, Dictionary<int, int> votes, int minimum)
        {
            var first = lines[0];
            var mode = ContainerModeRule.Decide(votes, string.Empty, minimum)!;
            var sizes = string.Join(";", votes.OrderBy(v => v.Key).Select(v => string.Create(CultureInfo.InvariantCulture, $"{v.Key}:{v.Value}")));
            var bandBooks = lines.Where(l => Math.Abs(Gram(l.Grams) - mode.Grams) <= ContainerModeRule.BandWidth * mode.Grams).Select(l => l.Book).Distinct().Count();
            var refusal = mode.Refusal == ContainerModeRule.FewProducts ? (tier == EraTier ? FewBooks : mode.Refusal)
                : mode.Refusal != null ? mode.Refusal
                : tier == BookTier && 2 * mode.Band <= mode.Products ? ContainerModeRule.NoClearMode
                : tier == EraTier && bandBooks < MinimumBooks ? FewBooks
                : null;
            return new CorpusSize(tier, scope, first.IngredientId, first.Kind, first.SizeWord, mode.Grams, lines.Count, lines.Select(l => l.Book).Distinct().Count(),
                mode.Band, mode.Products, mode.RunnerUp, lines.Min(l => l.Grams), lines.Max(l => l.Grams), sizes, refusal, bandBooks);
        }

        private static int Gram(decimal grams) => (int)Math.Round(grams, MidpointRounding.ToEven);

        /// <summary>
        /// The size a corpus tier gives one sized line with that line left out, as the hold-out asks:
        /// the book tier reads the other lines of the line's book, the era tier the lines of the other
        /// books of its era. Null when what is left gives no size.
        /// </summary>
        public static CorpusSize? LeaveOut(string tier, CorpusLine line, IEnumerable<CorpusLine> lines)
        {
            var key = lines.Where(l => l.IngredientId == line.IngredientId && l.Kind == line.Kind && l.SizeWord == line.SizeWord);
            var size = tier == BookTier
                ? BookSize(line.Book, key.Where(l => l.Book == line.Book && !ReferenceEquals(l, line)).ToList())
                : EraSize(line.Era, key.Where(l => l.Era == line.Era && l.Book != line.Book).ToList());
            return size is { Passes: true } ? size : null;
        }

        /// <summary>The source a corpus size row carries: tier, rule version, era, the size, how many held it (a book's lines, or an era's books) of how many, and the spread.</summary>
        public static string SourceRef(CorpusSize size, string era) =>
            (size.Tier == BookTier ? BookSourcePrefix : EraSourcePrefix)
            + string.Create(CultureInfo.InvariantCulture, $"{Version}:{era}:{size.Grams}g:{Held(size)}:{size.Min:0.#}-{size.Max:0.#}g");

        private static string Held(CorpusSize size) =>
            size.Tier == BookTier
                ? string.Create(CultureInfo.InvariantCulture, $"{size.Band}/{size.Votes}")
                : string.Create(CultureInfo.InvariantCulture, $"{size.BandBooks}/{size.Books}");

        /// <summary>The evidence a corpus size row keeps: the sizes stated and how often, lines, books and spread.</summary>
        public static string Evidence(CorpusSize size) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{(size.Tier == BookTier ? "lines" : "books")} by size (g) {size.Sizes}; {size.Lines} lines in {size.Books} books; mode {size.Grams} g on {size.Band} of {size.Votes}{(size.Tier == BookTier ? string.Empty : $" ({size.BandBooks} of {size.Books} books)")}, runner-up {size.RunnerUp}; spread {size.Min:0.#}-{size.Max:0.#} g; worst case {size.WorstCase:P1}");

        /// <summary>How a corpus size reads to a cook.</summary>
        public static string SizeText(CorpusSize size) =>
            string.Create(CultureInfo.InvariantCulture, $"{size.Grams / CanSizeTable.GramsPerOunce:0.##} oz ({size.Grams} g) {size.Kind}")
            + (size.SizeWord.Length > 0 ? ", " + size.SizeWord : string.Empty)
            + (size.Tier == BookTier
                ? string.Create(CultureInfo.InvariantCulture, $"; stated on {size.Band} of {size.Lines} sized lines of this book")
                : string.Create(CultureInfo.InvariantCulture, $"; the most common size {size.BandBooks} of {size.Books} {(size.Scope == ContainerEras.Unknown ? "undated books" : "books of the " + size.Scope + " era")} state, spread {size.Min:0.#}-{size.Max:0.#} g"));

        /// <summary>The class a corpus size is scored under: the tier and the container kind.</summary>
        public static string Class(string tier, string kind) => $"corpus-{tier}:{kind}";

        /// <summary>A short, stable token for a book in a key or a journal entry.</summary>
        public static string BookToken(string book) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(book)))[..12].ToLowerInvariant();

        /// <summary>The rows of a facts file (ops/container-era-facts.py writes the same columns from a capture of the corpus).</summary>
        public static IEnumerable<string> Facts(IEnumerable<CorpusSize> sizes, Func<long, string> food)
        {
            yield return "id\ttier\tscope\tfood\tkind\tsizeWord\tgrams\tlines\tbooks\tband\tvotes\trunnerUp\tmin\tmax\tsizes\tverdict";
            var n = 0;
            foreach (var s in sizes)
            {
                n++;
                yield return string.Create(CultureInfo.InvariantCulture,
                    $"f-{n:000}\t{s.Tier}\t{s.Scope}\t{food(s.IngredientId)}\t{s.Kind}\t{(s.SizeWord.Length == 0 ? "-" : s.SizeWord)}\t{s.Grams}\t{s.Lines}\t{s.Books}\t{s.Band}\t{s.Votes}\t{s.RunnerUp}\t{s.Min:0.#}\t{s.Max:0.#}\t{s.Sizes}\t{s.Refusal ?? "counts"}");
            }
        }
    }
}
