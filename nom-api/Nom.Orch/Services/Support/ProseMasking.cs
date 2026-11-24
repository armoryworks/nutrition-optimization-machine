using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>One ingredient line of a masked method: its token id (L1, L2, ...), the line, its head word and its name without amounts.</summary>
    public sealed record MaskedLine(string Id, string Line, string Head, string Name);

    /// <summary>One step of a masked method: the source step as prepared, its text with every amount and ingredient replaced by a token, and the tokens it holds.</summary>
    public sealed record MaskedStep(int Number, string Original, string Text, IReadOnlyList<string> Amounts, IReadOnlyList<string> Conversions, IReadOnlyList<string> Lines);

    /// <summary>
    /// A source method with every amount masked as a token (⟦Q1⟧ ... holding the exact source text)
    /// and every ingredient the recipe's lines name masked as a line token (⟦L1⟧ ...). The writer
    /// sees only tokens, so it can neither re-type an amount nor quote the source's numbers, and
    /// the tokens are put back by code (<see cref="Unmask"/>).
    /// </summary>
    public sealed record MaskedMethod(IReadOnlyList<MaskedStep> Steps, IReadOnlyDictionary<string, string> Amounts, IReadOnlyList<MaskedLine> Lines)
    {
        /// <summary>
        /// The text with every token put back: an amount as the source wrote it, a line token as the
        /// line's head word, led by the words of its name the method itself puts before it ("olive" of
        /// "olive oil") unless the written text already does. An unknown token is left as it is.
        /// </summary>
        public string Unmask(string text) => ProseMasking.Normalize(text) is var normal
            ? ProseMasking.TokenPattern.Replace(normal, m =>
            {
                var id = m.Groups["kind"].Value + m.Groups["n"].Value;
                if (Amounts.TryGetValue(id, out var amount)) return amount;
                if (Lines.FirstOrDefault(l => l.Id == id) is not { } line) return m.Value;
                var lead = Qualifier(id);
                if (lead.Length == 0 || normal[..m.Index].TrimEnd().EndsWith(lead, StringComparison.OrdinalIgnoreCase)) return line.Head;
                return lead + " " + line.Head;
            })
            : text;

        /// <summary>The words of a line's name that the masked method puts right before its token ("olive" in "olive ⟦L1⟧" for "olive oil"); empty when it puts none.</summary>
        public string Qualifier(string id)
        {
            if (Lines.FirstOrDefault(l => l.Id == id) is not { } line) return string.Empty;
            var nameWords = Regex.Matches(line.Name.ToLowerInvariant(), @"\p{L}+").Select(w => w.Value).ToHashSet(StringComparer.Ordinal);
            nameWords.Remove(line.Head.ToLowerInvariant());
            if (nameWords.Count == 0) return string.Empty;
            foreach (var step in Steps)
            {
                var m = Regex.Match(step.Text, @"((?:\p{L}+\s+){1,2})⟦" + id + "⟧");
                if (!m.Success) continue;
                var words = Regex.Matches(m.Groups[1].Value, @"\p{L}+").Select(w => w.Value).ToList();
                var kept = words.SkipWhile(w => !nameWords.Contains(w.ToLowerInvariant())).ToList();
                if (kept.Count > 0 && kept.All(w => nameWords.Contains(w.ToLowerInvariant()))) return string.Join(" ", kept);
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// The masking step of the clean-room prose lane (rules, no model): every amount
    /// <see cref="ProseQuantities"/> reads in a source step becomes an amount token, every word that
    /// names an ingredient line's head word (<see cref="ProseCheck.LineHeads"/>) becomes that line's
    /// token, and the step markers and cross-references <see cref="ProseQuantities.Prepare"/> drops
    /// are gone. <see cref="FormProblems"/> is the lane's form rule on written text: no digit and no
    /// amount outside the tokens. Text only: no database and no model.
    /// </summary>
    public static class ProseMasking
    {
        public const string Version = "mask/2";

        internal static readonly Regex TokenPattern = new(
            @"(?:⟦|\[\[|\[|\{\{|\{)\s*(?<kind>[QL])\s*(?<n>\d{1,3})\s*(?:⟧|\]\]|\]|\}\}|\})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Digit = new(@"[\p{N}¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Brackets = new(@"\([^()]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Amount(int n) => "⟦Q" + n.ToString(CultureInfo.InvariantCulture) + "⟧";

        public static string Line(string id) => "⟦" + id + "⟧";

        /// <summary>The text with every token a model may write (⟦Q1⟧, [Q1], [[Q1]], {Q1}) in the one form ⟦Q1⟧.</summary>
        public static string Normalize(string? text) =>
            TokenPattern.Replace(text ?? string.Empty, m => "⟦" + m.Groups["kind"].Value + int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "⟧");

        /// <summary>The token ids (Q1, L2, ...) a text holds, in order, repeats kept.</summary>
        public static IReadOnlyList<string> TokensIn(string? text) =>
            TokenPattern.Matches(Normalize(text)).Select(m => m.Groups["kind"].Value + m.Groups["n"].Value).ToList();

        /// <summary>Masks the source method's steps against the recipe's ingredient lines.</summary>
        public static MaskedMethod Mask(IReadOnlyList<string> steps, IReadOnlyList<string> lines)
        {
            var maskedLines = new List<MaskedLine>();
            foreach (var (line, head) in ProseCheck.LineHeads(lines))
            {
                if (maskedLines.Any(l => l.Head == head)) continue;
                maskedLines.Add(new MaskedLine("L" + (maskedLines.Count + 1).ToString(CultureInfo.InvariantCulture), line, head, NameOf(line)));
            }

            var amounts = new Dictionary<string, string>(StringComparer.Ordinal);
            var masked = new List<MaskedStep>();
            foreach (var step in steps)
            {
                var text = ProseQuantities.Prepare(step);
                if (text.Length == 0) continue;
                var spans = ProseQuantities.ReadPrepared(text)
                    .GroupBy(q => (q.Start, q.End))
                    .Select(g => (g.Key.Start, g.Key.End, Conversion: g.All(q => q.Conversion)))
                    .OrderBy(s => s.Start)
                    .ToList();

                var ids = new List<(string Id, bool Conversion)>();
                var built = new System.Text.StringBuilder();
                var at = 0;
                foreach (var span in spans)
                {
                    if (span.Start < at) continue;
                    built.Append(MaskLines(text[at..span.Start], maskedLines));
                    var id = "Q" + (amounts.Count + 1).ToString(CultureInfo.InvariantCulture);
                    amounts[id] = text[span.Start..span.End];
                    ids.Add((id, span.Conversion));
                    built.Append('⟦').Append(id).Append('⟧');
                    at = span.End;
                }

                built.Append(MaskLines(text[at..], maskedLines));
                var maskedText = built.ToString();
                var lineIds = TokensIn(maskedText).Where(t => t[0] == 'L').Distinct().ToList();
                masked.Add(new MaskedStep(masked.Count + 1, text, maskedText,
                    ids.Where(i => !i.Conversion).Select(i => i.Id).ToList(),
                    ids.Where(i => i.Conversion).Select(i => i.Id).ToList(),
                    lineIds));
            }

            return new MaskedMethod(masked, amounts, maskedLines);
        }

        /// <summary>
        /// What breaks the lane's form rule in a written (still masked) text: a digit or a fraction
        /// outside the tokens, or an amount <see cref="ProseQuantities"/> reads outside them ("a
        /// minute", "two cups"). Empty when the text keeps to the tokens.
        /// </summary>
        public static IReadOnlyList<string> FormProblems(string? text)
        {
            var problems = new List<string>();
            var stripped = TokenPattern.Replace(Normalize(text), " , ");
            if (Digit.IsMatch(stripped)) problems.Add("a digit outside the amount tokens");
            foreach (var q in ProseQuantities.Read(stripped).Where(q => !q.Soft))
                problems.Add($"an amount of its own (\"{q.Text}\") outside the amount tokens");
            return problems;
        }

        private static string MaskLines(string text, List<MaskedLine> lines)
        {
            if (lines.Count == 0) return text;
            return Word.Replace(text, m =>
            {
                var word = m.Value.ToLowerInvariant();
                var line = lines.FirstOrDefault(l => ProseCheck.NamesHead(word, l.Head));
                return line == null ? m.Value : Line(line.Id);
            });
        }

        private static string NameOf(string line)
        {
            var prepared = ProseQuantities.Prepare(line);
            var text = Brackets.Replace(ProseQuantities.WithoutAmounts(prepared, ProseQuantities.ReadPrepared(prepared)), " ");
            var comma = text.IndexOf(',');
            if (comma > 0) text = text[..comma];
            text = Digit.Replace(text, " ");
            return Regex.Replace(text, @"\s+", " ").Trim().Trim('-', '.', ' ');
        }
    }
}
