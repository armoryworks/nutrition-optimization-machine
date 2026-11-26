using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The copy rule of the prose check from check v7 on, under the owner's standard of 2026-11-23: the
    /// unit judged is the sentence, read in its step and in the whole text. Each sentence of a text (its
    /// description and its steps) is set beside the source passage it follows most closely (one source
    /// sentence, two adjacent ones it may merge, or the part of one it may split off), and compared word
    /// by word with the source's facts set aside (<see cref="IsFact"/>: amounts and numbers, units, the
    /// words of the recipe's ingredient lines, and the craft's own words of <see cref="CraftWords"/>).
    /// A sentence of <see cref="MinWords"/> words or more is a copy (<see cref="Sentence.Copy"/>) when it is the source's sentence with single
    /// words swapped, dropped, added or moved (no change longer than <see cref="MaxEdit"/> words, at
    /// least <see cref="MinSimilarity"/> of it in the source's order) and keeps at least
    /// <see cref="MinFrame"/> of the source's own words beyond the facts, or when it holds a word-for-word
    /// run of at least <see cref="MinFrameRun"/> of them. A sentence that is mostly facts (an oven
    /// temperature, a short list of foods into a bowl) keeps too few of the source's own words to be a
    /// copy, and a sentence rebuilt from understanding (another frame, clauses rebuilt, merged or split)
    /// changes more than single words. A sentence of <see cref="VerbatimMinWords"/> words or more kept word
    /// for word or with a word or two changed (<see cref="Sentence.NearVerbatim"/>) counts with the copies
    /// toward the whole text, however few of its words are the source's own, since a method made of the
    /// source's sentences is the source's text: a text is mostly copied (<see cref="Reading.Mostly"/>) when
    /// it holds a copy and at least <see cref="MostlyCopied"/> of its sentences are copies or kept so, or
    /// when <see cref="MostlyVerbatimSentences"/> or more and at least <see cref="MostlyVerbatim"/> of them
    /// are kept so;
    /// otherwise some or none of it is copied; the verdict orders the rewrite lane's queue. The lines are set on the judged rows
    /// of prose-check.tsv. Text only: no database and no model.
    /// </summary>
    public static class ProseSentences
    {
        public const string Version = "sentences/1";

        public const int MinWords = 10;
        public const int MaxEdit = 2;
        public const decimal MinSimilarity = 0.6m;
        public const int MinFrame = 6;
        public const int MinFrameRun = 8;
        public const decimal VerbatimSimilarity = 0.8m;
        public const int VerbatimMaxEdit = 2;
        public const int VerbatimMinWords = 5;

        /// <summary>The share of a text's sentences that are copies or kept nearly word for word at or over which the text is mostly copied and withheld as a whole.</summary>
        public const decimal MostlyCopied = 0.4m;

        /// <summary>The share of a text's sentences kept nearly word for word at or over which the text is mostly copied even when none of them is long enough to be a copy: a method made of the source's sentences.</summary>
        public const decimal MostlyVerbatim = 0.5m;

        /// <summary>The sentences kept nearly word for word a text needs before <see cref="MostlyVerbatim"/> reads it as the source's text: one or two short sentences can hardly be said another way.</summary>
        public const int MostlyVerbatimSentences = 4;

        /// <summary>The words of the craft that are facts of a method, not wording: heat, the oven and its scale, vessels and tools, and the signs of doneness a cook reads.</summary>
        public static readonly IReadOnlySet<string> CraftWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "oven", "preheat", "preheated", "heat", "low", "medium", "high", "temperature", "room", "degree", "degrees", "fahrenheit", "celsius", "f", "c",
            "pan", "pans", "skillet", "skillets", "saucepan", "pot", "pots", "bowl", "bowls", "sheet", "sheets", "baking", "parchment", "paper", "foil", "rack", "wire",
            "dish", "tray", "mixer", "processor", "blender", "food", "stand", "whisk", "spatula", "spoon", "fork", "knife", "lid", "plastic", "wrap", "airtight",
            "container", "refrigerator", "fridge", "freezer", "microwave", "stove", "stovetop", "grill", "broiler", "slow", "cooker", "instant",
            "golden", "brown", "tender", "smooth", "crisp", "crispy", "thick", "thickened", "fluffy", "creamy", "melted", "bubbly", "boil", "boiling", "simmer", "simmering",
            "set", "firm", "combined", "large", "small",
        };

        private static readonly HashSet<string> LineStopWords = new(StringComparer.Ordinal)
        {
            "a", "an", "the", "and", "or", "of", "to", "in", "on", "with", "for", "into", "onto", "over", "at", "by", "from", "until", "then", "is", "are", "be",
            "it", "its", "this", "that", "your", "you", "plus", "more", "about", "optional", "divided", "taste", "if", "needed", "as", "such", "use", "using",
        };

        private static readonly Regex SentenceEnd = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>One sentence of a text as the rule read it: where it stands (part 0 the description, part n step n), its words, and how it sits beside the closest source passage.</summary>
        public sealed record Sentence(int Part, int Index, string Text, int Words, decimal Similarity, int Edit, int Frame, int FrameRun, string? Closest)
        {
            /// <summary>The source's sentence lightly edited, or a word-for-word run of the source's own wording.</summary>
            public bool Copy => (Words >= MinWords && Edit <= MaxEdit && Similarity >= MinSimilarity && Frame >= MinFrame) || FrameRun >= MinFrameRun;

            /// <summary>Kept word for word or with a word or two changed, however few of its words are the source's own.</summary>
            public bool NearVerbatim => Words >= VerbatimMinWords && Similarity >= VerbatimSimilarity && Edit <= VerbatimMaxEdit;

            public string Label => (Part == 0 ? "description" : "step " + Part.ToString(CultureInfo.InvariantCulture)) + " sentence " + Index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The sentences of a text and what the rule makes of them.</summary>
        public sealed record Reading(IReadOnlyList<Sentence> Sentences)
        {
            public static readonly Reading None = new(Array.Empty<Sentence>());

            public IReadOnlyList<Sentence> Copies => Sentences.Where(s => s.Copy).ToList();

            public int Copied => Sentences.Count(s => s.Copy);

            public int NearVerbatim => Sentences.Count(s => s.NearVerbatim);

            /// <summary>The sentences that are copies or kept nearly word for word.</summary>
            public int Kept => Sentences.Count(s => s.Copy || s.NearVerbatim);

            public decimal KeptShare => Sentences.Count == 0 ? 0m : Math.Round((decimal)Kept / Sentences.Count, 3);

            /// <summary>Mostly the source's sentences lightly edited or kept: first in the rewrite lane's queue.</summary>
            public bool Mostly => (Copied > 0 && KeptShare >= MostlyCopied) || (NearVerbatim >= MostlyVerbatimSentences && VerbatimShare >= MostlyVerbatim);

            public decimal VerbatimShare => Sentences.Count == 0 ? 0m : Math.Round((decimal)NearVerbatim / Sentences.Count, 3);

            public override string ToString() =>
                string.Create(CultureInfo.InvariantCulture, $"{Copied} of {Sentences.Count} sentences copied, {Kept} copied or kept nearly word for word ({KeptShare:0.###}), {NearVerbatim} kept nearly word for word ({VerbatimShare:0.###})");
        }

        /// <summary>The sentences of a prepared text, split after a full stop, a question or exclamation mark or a semicolon.</summary>
        public static IReadOnlyList<string> Split(string? text) =>
            string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : SentenceEnd.Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>Reads every sentence of <paramref name="rewrite"/> (description, then each step) against the description and method of <paramref name="source"/>, with the recipe's ingredient lines as facts.</summary>
        public static Reading Read(RewriteFidelity.SourceText source, RewriteFidelity.SourceText rewrite, IReadOnlyList<string>? lines = null)
        {
            var facts = LineWords(lines ?? Array.Empty<string>());
            var sourceSentences = new[] { source.Description }.Concat(source.Steps)
                .SelectMany(t => Split(ProseQuantities.Prepare(t)))
                .Select(ProseCheck.CopyTokens)
                .Where(t => t.Count > 0)
                .ToList();
            var parts = new[] { rewrite.Description }.Concat(rewrite.Steps).ToList();
            var sentences = new List<Sentence>();
            for (var p = 0; p < parts.Count; p++)
            {
                var index = 0;
                foreach (var text in Split(ProseQuantities.Prepare(parts[p])))
                {
                    var tokens = ProseCheck.CopyTokens(text);
                    if (tokens.Count == 0) continue;
                    sentences.Add(Judge(p, ++index, text, tokens, sourceSentences, facts));
                }
            }

            return new Reading(sentences);
        }

        /// <summary>The words of the ingredient lines that are facts in a method: every word but amounts, units and a few function words, singular and plural.</summary>
        public static IReadOnlySet<string> LineWords(IReadOnlyList<string> lines)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                foreach (var word in ProseCheck.CopyTokens(ProseQuantities.Prepare(line)))
                {
                    if (word == ProsePhrases.Number || LineStopWords.Contains(word) || ProseQuantities.IsUnit(word)) continue;
                    words.Add(word);
                    words.Add(word + "s");
                    if (word.Length > 3 && word.EndsWith('s')) words.Add(word[..^1]);
                }
            }

            return words;
        }

        /// <summary>Whether a word of a sentence is a fact rather than wording: a number or amount, a unit, a word of the ingredient lines, or a word of the craft.</summary>
        public static bool IsFact(string word, IReadOnlySet<string> lineWords) =>
            word == ProsePhrases.Number || ProseQuantities.IsUnit(word) || CraftWords.Contains(word) || lineWords.Contains(word);

        private static Sentence Judge(int part, int index, string text, List<string> tokens, List<List<string>> source, IReadOnlySet<string> facts)
        {
            var best = (Similarity: -1m, Frame: -1, Edit: 0, FrameRun: 0, Closest: (string?)null);
            for (var i = 0; i < source.Count; i++)
            {
                for (var j = i; j <= Math.Min(i + 1, source.Count - 1); j++)
                {
                    var passage = j == i ? source[i] : source[i].Concat(source[j]).ToList();
                    var pairs = Align(tokens, passage);
                    if (pairs.Count == 0) continue;
                    var span = pairs[^1].B - pairs[0].B + 1;
                    var similarity = Math.Round((decimal)pairs.Count / Math.Max(tokens.Count, span), 3);
                    var frame = pairs.Count(p => !IsFact(tokens[p.A], facts));
                    if (similarity < best.Similarity || (similarity == best.Similarity && frame <= best.Frame)) continue;
                    var edit = Math.Max(pairs[0].A, tokens.Count - 1 - pairs[^1].A);
                    var run = 0;
                    var longest = 0;
                    for (var k = 0; k < pairs.Count; k++)
                    {
                        if (k > 0)
                        {
                            edit = Math.Max(edit, Math.Max(pairs[k].A - pairs[k - 1].A - 1, pairs[k].B - pairs[k - 1].B - 1));
                            if (pairs[k].A != pairs[k - 1].A + 1 || pairs[k].B != pairs[k - 1].B + 1) run = 0;
                        }

                        if (!IsFact(tokens[pairs[k].A], facts)) run++;
                        longest = Math.Max(longest, run);
                    }

                    best = (similarity, frame, edit, longest, string.Join(" ", passage));
                }
            }

            return best.Closest == null
                ? new Sentence(part, index, text, tokens.Count, 0m, tokens.Count, 0, 0, null)
                : new Sentence(part, index, text, tokens.Count, best.Similarity, best.Edit, best.Frame, best.FrameRun, best.Closest);
        }

        /// <summary>A longest common subsequence of two word lists as index pairs in order.</summary>
        internal static List<(int A, int B)> Align(List<string> a, List<string> b)
        {
            var n = a.Count;
            var m = b.Count;
            var table = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            {
                for (var j = m - 1; j >= 0; j--)
                    table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }

            var pairs = new List<(int, int)>();
            var x = 0;
            var y = 0;
            while (x < n && y < m)
            {
                if (a[x] == b[y])
                {
                    pairs.Add((x, y));
                    x++;
                    y++;
                }
                else if (table[x + 1, y] >= table[x, y + 1])
                {
                    x++;
                }
                else
                {
                    y++;
                }
            }

            return pairs;
        }
    }
}
