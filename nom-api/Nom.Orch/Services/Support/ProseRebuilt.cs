using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The strict copy check of the reword lane's own new text, on the owner's standard of 2026-11-23 as the lead
    /// read it after the proof of 2026-11-24: a reworded step sentence passes only when it is clearly rebuilt (another
    /// frame, its clauses rebuilt, merged or split). Each sentence is set beside every source passage (one source
    /// sentence or two adjacent ones), with its words compared in a plain form (<see cref="Plain"/>: a closed list of
    /// equivalents and a stem), so the source's sentence with words swapped for synonyms in the same frame, dropped or
    /// moved still reads as the source's. A sentence is a copy (<see cref="Sentence.Copy"/>) when, beside some passage,
    /// <see cref="MaxFrame"/> or more of its frame (every word but the facts of <see cref="ProseSentences.IsFact"/>)
    /// follows the passage in order; or, with <see cref="MinOwnWords"/> or more words of its own wording (the frame
    /// without function words), <see cref="MaxOwn"/> or more of that wording follows the passage in order; or, with
    /// <see cref="MinOverlapWords"/> or more of its own words in the passage in any order, those are
    /// <see cref="MaxOverlap"/> or more of its own wording. A sentence that can hardly be said another way is a
    /// formula (<see cref="Sentence.Formula"/>: its own wording is at most <see cref="FormulaWords"/> words of the
    /// closed list <see cref="FormulaWord"/>) and passes unless <see cref="FormulaSimilarity"/> or more of all its words
    /// follow a passage in order. The lines are set on the judged step sentences of the two proof draws of 2026-11-24
    /// so that no sentence judged a copy passes; a false copy verdict only costs the lane a retry. Text only: no
    /// database and no model.
    /// </summary>
    public static class ProseRebuilt
    {
        public const string Version = "rebuilt/1";

        public const decimal MaxFrame = 0.5m;
        public const decimal MaxOwn = 0.5m;
        public const int MinOwnWords = 2;
        public const decimal MaxOverlap = 0.6m;
        public const int MinOverlapWords = 3;
        public const decimal FormulaSimilarity = 0.5m;
        public const int FormulaWords = 3;
        public const int StrayWords = 2;
        public const int StrayGap = 4;

        /// <summary>The words that carry no wording of their own: articles, pronouns, the commonest prepositions and joins.</summary>
        public static readonly IReadOnlySet<string> Function = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "by", "for", "with", "into", "onto", "over", "from", "up", "down", "out", "off",
            "it", "its", "them", "they", "this", "that", "these", "those", "your", "you", "is", "are", "be", "will", "can", "then", "if", "as", "so", "all", "each",
        };

        /// <summary>
        /// The closed list of formula words: a sentence whose own wording is only these (at most <see cref="FormulaWords"/>)
        /// beside its facts is a formula that can hardly be said another way ("Preheat the oven to 350°F", "Season with salt
        /// and pepper", "Serve warm", "In a large bowl, whisk the flour, sugar and salt").
        /// </summary>
        public static readonly IReadOnlySet<string> FormulaWord = new HashSet<string>(StringComparer.Ordinal)
        {
            "add", "stir", "mix", "combine", "whisk", "beat", "fold", "season", "taste", "serve", "enjoy", "garnish", "top", "sprinkle", "drizzle",
            "bake", "cook", "fry", "roast", "grill", "boil", "simmer", "cool", "chill", "refrigerate", "freeze", "store", "cover", "drain", "rinse",
            "slice", "chop", "dice", "cut", "peel", "line", "grease", "preheat", "together", "until", "warm", "hot", "cold", "immediately", "aside",
            "repeat", "remaining", "minute", "minutes", "hour", "hours", "about", "more", "well", "again", "place", "put", "pour", "transfer", "remove",
        };

        /// <summary>One sentence of new text as the check read it beside the source passages it follows most closely: each measure is its highest over the passages.</summary>
        public sealed record Sentence(int Part, int Index, string Text, int Words, decimal Similarity, decimal FrameSimilarity, int Own, decimal OwnSimilarity, int Overlap, bool Formula, string? Closest)
        {
            /// <summary>The share of the sentence's own wording (beyond the facts and function words) found in a passage, in any order.</summary>
            public decimal OverlapShare => Own == 0 ? 0m : Math.Round((decimal)Overlap / Own, 3);

            /// <summary>The source's sentence lightly edited: see <see cref="ProseRebuilt"/>.</summary>
            public bool Copy => Closest != null && (Formula
                ? Similarity >= FormulaSimilarity
                : FrameSimilarity >= MaxFrame || (Own >= MinOwnWords && OwnSimilarity >= MaxOwn) || (Overlap >= MinOverlapWords && OverlapShare >= MaxOverlap));

            public string Label => (Part == 0 ? "description" : "step " + Part.ToString(CultureInfo.InvariantCulture)) + " sentence " + Index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Reads every sentence of <paramref name="text"/> (description, then each step) against the description and method of <paramref name="source"/>, with the recipe's ingredient lines as facts.</summary>
        public static IReadOnlyList<Sentence> Read(RewriteFidelity.SourceText source, RewriteFidelity.SourceText text, IReadOnlyList<string>? lines = null)
        {
            var facts = ProseSentences.LineWords(lines ?? Array.Empty<string>());
            var passages = new[] { source.Description }.Concat(source.Steps)
                .SelectMany(t => ProseSentences.Split(ProseQuantities.Prepare(t)))
                .Select(ProseCheck.CopyTokens)
                .Where(t => t.Count > 0)
                .Select(t => (Plain: t.Select(Plain).ToList(), Own: t.Where(w => IsOwn(w, facts)).Select(Plain).ToList()))
                .ToList();
            var parts = new[] { text.Description }.Concat(text.Steps).ToList();
            var sentences = new List<Sentence>();
            for (var p = 0; p < parts.Count; p++)
            {
                var index = 0;
                foreach (var sentence in ProseSentences.Split(ProseQuantities.Prepare(parts[p])))
                {
                    var tokens = ProseCheck.CopyTokens(sentence);
                    if (tokens.Count == 0) continue;
                    sentences.Add(Judge(p, ++index, sentence, tokens, passages, facts));
                }
            }

            return sentences;
        }

        /// <summary>Whether every sentence of <paramref name="text"/> is clearly rebuilt from <paramref name="source"/>.</summary>
        public static bool AllRebuilt(RewriteFidelity.SourceText source, RewriteFidelity.SourceText text, IReadOnlyList<string>? lines = null) =>
            Read(source, text, lines).All(s => !s.Copy);

        /// <summary>Whether a word of a sentence is its own wording: neither a fact nor a function word.</summary>
        public static bool IsOwn(string word, IReadOnlySet<string> lineWords) => !ProseSentences.IsFact(word, lineWords) && !Function.Contains(word);

        private static Sentence Judge(int part, int index, string text, List<string> tokens, List<(List<string> Plain, List<string> Own)> passages, IReadOnlySet<string> facts)
        {
            var plain = tokens.Select(Plain).ToList();
            var own = Enumerable.Range(0, tokens.Count).Where(i => IsOwn(tokens[i], facts)).ToList();
            var formula = own.Count <= FormulaWords && own.All(i => FormulaWord.Contains(plain[i]) || FormulaWord.Contains(tokens[i]));
            var ownPlain = own.Select(i => plain[i]).ToList();
            var frame = Enumerable.Range(0, tokens.Count).Where(i => !ProseSentences.IsFact(tokens[i], facts)).Select(i => plain[i]).ToList();
            var best = (Similarity: 0m, Frame: 0m, Own: 0m, Overlap: 0, Closest: (string?)null, Score: -1m);
            for (var i = 0; i < passages.Count; i++)
            {
                for (var j = i; j <= Math.Min(i + 1, passages.Count - 1); j++)
                {
                    var passage = j == i ? passages[i].Plain : passages[i].Plain.Concat(passages[j].Plain).ToList();
                    var ownPassage = j == i ? passages[i].Own : passages[i].Own.Concat(passages[j].Own).ToList();
                    var framePassage = passage.Where(w => !ProseSentences.IsFact(w, facts)).ToList();
                    var similarity = Closeness(plain, passage);
                    var frameSimilarity = Closeness(frame, framePassage);
                    var words = passage.ToHashSet(StringComparer.Ordinal);
                    var overlap = ownPlain.Count(words.Contains);
                    var score = similarity + frameSimilarity;
                    best = (Math.Max(best.Similarity, similarity), Math.Max(best.Frame, frameSimilarity), Math.Max(best.Own, Closeness(ownPlain, ownPassage)),
                        Math.Max(best.Overlap, overlap), score > best.Score ? string.Join(" ", passage) : best.Closest, Math.Max(score, best.Score));
                }
            }

            return new Sentence(part, index, text, tokens.Count, best.Similarity, best.Frame, own.Count, best.Own, best.Overlap, formula, best.Closest);
        }

        /// <summary>How closely a word list follows a passage in order: the longest common subsequence, strays at either end trimmed, over the longer of the list and the passage span it covers.</summary>
        private static decimal Closeness(List<string> words, List<string> passage)
        {
            if (words.Count == 0 || passage.Count == 0) return 0m;
            var pairs = Trim(ProseSentences.Align(words, passage));
            if (pairs.Count == 0) return 0m;
            return Math.Round((decimal)pairs.Count / Math.Max(words.Count, pairs[^1].B - pairs[0].B + 1), 3);
        }

        /// <summary>The form of a word the check compares: a word of a method's wording that has a plain equivalent (<see cref="Equivalents"/>) is compared as that word, and an inflected word as its stem, so a sentence with single words swapped for their equivalents still reads as the source's.</summary>
        public static string Plain(string word)
        {
            if (Equivalents.TryGetValue(word, out var plain)) return plain;
            var stem = word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal) ? word[..^3]
                : word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal) ? word[..^2]
                : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1]
                : word;
            if (Equivalents.TryGetValue(stem, out plain) || Equivalents.TryGetValue(stem + "e", out plain)) return plain;
            if (stem.Length > 2 && stem[^1] == stem[^2] && stem[^1] is not ('l' or 's' or 'e')) stem = stem[..^1];
            return Equivalents.TryGetValue(stem, out plain) ? plain : stem;
        }

        /// <summary>
        /// Words of a method's wording that say the same thing in the same place of a sentence, each read as the first of its group:
        /// the swaps a light edit makes (place for add, combine for mix, lower for reduce, allow for let, additional for more), and so
        /// not a rebuilt sentence. A fact (an amount, a food, a word of the craft) is never one of them.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Equivalents = Groups(
            new[] { "add", "place", "put", "pour", "include", "transfer", "lay", "arrange", "move" },
            new[] { "mix", "stir", "combine", "blend", "toss", "incorporate" },
            new[] { "lower", "reduce", "decrease" },
            new[] { "let", "allow" },
            new[] { "remove", "take" },
            new[] { "more", "additional", "extra", "another", "further" },
            new[] { "until", "till" },
            new[] { "about", "around", "approximately", "roughly" },
            new[] { "once", "after", "when" },
            new[] { "while", "meanwhile" },
            new[] { "chill", "refrigerate", "cool" },
            new[] { "serve", "enjoy" },
            new[] { "form", "shape" },
            new[] { "desire", "prefer", "need", "necessary", "require", "wish", "like" },
            new[] { "gradually", "slowly" },
            new[] { "release", "loosen" },
            new[] { "wipe", "clean" },
            new[] { "spray", "grease", "coat" },
            new[] { "evenly", "equally" },
            new[] { "completely", "fully", "thoroughly", "entirely" },
            new[] { "begin", "start" },
            new[] { "make", "prepare", "create" },
            new[] { "use", "using", "with" },
            new[] { "accord", "according", "per", "following" },
            new[] { "direction", "instruction" },
            new[] { "store", "keep", "save" },
            new[] { "ahead", "advance" },
            new[] { "tasty", "delicious", "deliciou", "flavorful", "yummy" },
            new[] { "quick", "fast", "speedy" },
            new[] { "easy", "simple", "effortless" },
            new[] { "perfect", "ideal", "great" },
            new[] { "favorite", "preferred" },
            new[] { "drop", "spoon", "scoop" },
            new[] { "rest", "stand", "sit" },
            new[] { "gently", "lightly", "carefully" },
            new[] { "flip", "turn" },
            new[] { "pick", "choose", "select" },
            new[] { "look", "appear", "seem", "resemble" });

        private static Dictionary<string, string> Groups(params string[][] groups)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                foreach (var word in group) map[word] = group[0];
            }

            return map;
        }

        /// <summary>The alignment without a stray match at either end: <see cref="StrayWords"/> or fewer matched words cut off from the rest by a gap of more than <see cref="StrayGap"/> words, such as a "the" of an earlier clause the sentence does not follow.</summary>
        private static List<(int A, int B)> Trim(List<(int A, int B)> pairs)
        {
            static int Gap((int A, int B) x, (int A, int B) y) => Math.Max(y.A - x.A - 1, y.B - x.B - 1);
            if (pairs.Count == 0) return pairs;
            var start = 0;
            var end = pairs.Count - 1;
            while (end - start > StrayWords)
            {
                var k = start;
                while (k < end && Gap(pairs[k], pairs[k + 1]) <= StrayGap) k++;
                if (k == end || k - start + 1 > StrayWords) break;
                start = k + 1;
            }

            while (end - start > StrayWords)
            {
                var k = end;
                while (k > start && Gap(pairs[k - 1], pairs[k]) <= StrayGap) k--;
                if (k == start || end - k + 1 > StrayWords) break;
                end = k - 1;
            }

            return pairs.GetRange(start, end - start + 1);
        }
    }
}
