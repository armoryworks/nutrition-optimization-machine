using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Decides whether a recipe's rewritten prose may leave the source-prose quarantine. Every
    /// amount, time and temperature of the original method (digits, fractions and spelled-out
    /// numbers) must still be in the rewrite, the rewrite may state no number the original lacks,
    /// no sentence of <see cref="CopiedSentenceWords"/> or more words and no run of
    /// <see cref="CopiedRunWords"/> words may be copied whole from the original, and the text as a
    /// whole must have changed. A temperature in brackets after another ("350°F (175°C)") may be
    /// dropped but not invented, and step numbers ("2. Make the salad") are not amounts. Text
    /// only: no database and no model.
    /// </summary>
    public static class RewriteFidelity
    {
        public const int CopiedSentenceWords = 10;
        public const int CopiedRunWords = 20;

        public const string LostNumber = "lost-number";
        public const string AddedNumber = "added-number";
        public const string CopiedText = "copied-text";
        public const string Unchanged = "unchanged";

        private const string Fractions = "¼½¾⅓⅔⅛⅜⅝⅞";

        private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Apostrophes = new(@"['’]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SentenceEnd = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StepNumber = new(
            @"(?<=^|[.!?:]\s*)\s*(?:step\s*)?\d{1,2}\s*[.)](?=\s*\p{L})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BracketedTemperature = new(
            @"\([^()]*\d\s*(?:°|º|degrees?\b)[^()]*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex NumberPattern = new(
            @"(?<whole>\d+)(?:\s+|-)?(?<part>\d+/\d+|[" + Fractions + @"])"
            + @"|(?<fraction>\d+/\d+)"
            + @"|(?<thousands>\d{1,3}(?:,\d{3})+)(?!\d)"
            + @"|(?<plain>\d+(?:\.\d+)?)"
            + @"|(?<glyph>[" + Fractions + @"])"
            + @"|\b(?<tens>twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:[- ](?<ones>one|two|three|four|five|six|seven|eight|nine))?\b"
            + @"|\b(?<word>two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|dozen)\b"
            + @"|\b(?<soft>one|half|quarter)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, decimal> Words = new(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1m, ["two"] = 2m, ["three"] = 3m, ["four"] = 4m, ["five"] = 5m, ["six"] = 6m, ["seven"] = 7m, ["eight"] = 8m,
            ["nine"] = 9m, ["ten"] = 10m, ["eleven"] = 11m, ["twelve"] = 12m, ["thirteen"] = 13m, ["fourteen"] = 14m, ["fifteen"] = 15m,
            ["sixteen"] = 16m, ["seventeen"] = 17m, ["eighteen"] = 18m, ["nineteen"] = 19m, ["dozen"] = 12m,
            ["twenty"] = 20m, ["thirty"] = 30m, ["forty"] = 40m, ["fifty"] = 50m, ["sixty"] = 60m, ["seventy"] = 70m, ["eighty"] = 80m,
            ["ninety"] = 90m, ["half"] = 0.5m, ["quarter"] = 0.25m,
        };

        private static readonly Dictionary<char, decimal> Glyphs = new()
        {
            ['¼'] = 0.25m, ['½'] = 0.5m, ['¾'] = 0.75m, ['⅓'] = 1m / 3m, ['⅔'] = 2m / 3m,
            ['⅛'] = 0.125m, ['⅜'] = 0.375m, ['⅝'] = 0.625m, ['⅞'] = 0.875m,
        };

        /// <summary>What the check found; <see cref="Passed"/> when nothing.</summary>
        public sealed record Finding(IReadOnlyList<string> Lost, IReadOnlyList<string> Added, string? Copied, bool IsUnchanged)
        {
            public bool Passed => Lost.Count == 0 && Added.Count == 0 && Copied == null && !IsUnchanged;

            public IReadOnlyList<string> Reasons =>
                new[]
                {
                    Lost.Count > 0 ? LostNumber : null,
                    Added.Count > 0 ? AddedNumber : null,
                    Copied != null ? CopiedText : null,
                    IsUnchanged ? Unchanged : null,
                }.OfType<string>().ToList();

            public override string ToString() =>
                Passed ? "passed" : string.Join("; ", new[]
                {
                    Lost.Count > 0 ? $"lost {string.Join(", ", Lost)}" : null,
                    Added.Count > 0 ? $"added {string.Join(", ", Added)}" : null,
                    Copied != null ? $"copied \"{Copied}\"" : null,
                    IsUnchanged ? "unchanged" : null,
                }.OfType<string>());
        }

        /// <summary>A scraped recipe's own description and method, as its stored JSON-LD gives them.</summary>
        public sealed record SourceText(string Description, IReadOnlyList<string> Steps);

        public static Finding Check(SourceText original, SourceText rewrite)
        {
            var method = original.Steps.Select(Prepare).Where(t => t.Length > 0).ToList();
            var description = Prepare(original.Description);
            var before = method.Prepend(description).Where(t => t.Length > 0).ToList();
            var after = rewrite.Steps.Prepend(rewrite.Description).Select(Prepare).Where(t => t.Length > 0).ToList();

            var required = new HashSet<string>(StringComparer.Ordinal);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in before)
            {
                var bracketed = BracketedTemperature.Matches(text).Select(m => m.Value).ToList();
                var rest = BracketedTemperature.Replace(text, " ");
                foreach (var (value, soft) in Numbers(rest))
                {
                    allowed.Add(value);
                    if (!soft && method.Contains(text)) required.Add(value);
                }

                foreach (var (value, _) in bracketed.SelectMany(Numbers)) allowed.Add(value);
            }

            var stated = new HashSet<string>(StringComparer.Ordinal);
            var statedStrong = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (value, soft) in after.SelectMany(Numbers))
            {
                stated.Add(value);
                if (!soft) statedStrong.Add(value);
            }

            var lost = required.Where(v => !stated.Contains(v)).OrderBy(Order).ToList();
            var added = statedStrong.Where(v => !allowed.Contains(v)).OrderBy(Order).ToList();

            var rewritten = after.Select(t => " " + string.Join(" ", WordsOf(t)) + " ").ToList();
            var copied = before
                .SelectMany(t => SentenceEnd.Split(t))
                .Select(WordsOf)
                .Where(w => w.Count >= CopiedSentenceWords)
                .Select(w => string.Join(" ", w))
                .FirstOrDefault(s => rewritten.Any(r => r.Contains(" " + s + " ", StringComparison.Ordinal)));
            if (copied == null)
            {
                var runs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var text in before) runs.UnionWith(Runs(WordsOf(text)));
                copied = after.SelectMany(t => Runs(WordsOf(t))).FirstOrDefault(runs.Contains);
            }

            var beforeWords = string.Join(" ", before.SelectMany(WordsOf));
            var afterWords = string.Join(" ", after.SelectMany(WordsOf));
            var unchanged = beforeWords.Length > 0 && beforeWords == afterWords;

            return new Finding(lost, added, copied, unchanged);
        }

        /// <summary>
        /// The description and the method steps of a schema.org Recipe node, sections flattened in
        /// order; null when the node cannot be read or states no method.
        /// </summary>
        public static SourceText? Source(string? rawJsonLd)
        {
            if (string.IsNullOrWhiteSpace(rawJsonLd)) return null;
            try
            {
                using var document = JsonDocument.Parse(rawJsonLd);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString() ?? string.Empty
                    : string.Empty;
                var steps = new List<string>();
                if (root.TryGetProperty("recipeInstructions", out var instructions)) Collect(instructions, steps);
                return steps.Count == 0 ? null : new SourceText(Clean(description), steps);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void Collect(JsonElement element, List<string> steps)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    if (Clean(element.GetString()) is { Length: > 0 } step) steps.Add(step);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Collect(item, steps);
                    break;
                case JsonValueKind.Object:
                    if (element.TryGetProperty("itemListElement", out var items))
                    {
                        Collect(items, steps);
                    }
                    else if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        Collect(text, steps);
                    }
                    else if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        Collect(name, steps);
                    }

                    break;
            }
        }

        private static string Clean(string? text) =>
            Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(text ?? string.Empty, " ")), @"\s+", " ").Trim();

        private static string Prepare(string? text) => StepNumber.Replace(Clean(text), " ").Trim();

        private static IEnumerable<(string Value, bool Soft)> Numbers(string text)
        {
            foreach (Match m in NumberPattern.Matches(text))
            {
                if (m.Groups["whole"].Success)
                {
                    yield return (Format(Integer(m.Groups["whole"].Value) + Part(m.Groups["part"].Value)), false);
                }
                else if (m.Groups["fraction"].Success)
                {
                    yield return (Format(Part(m.Groups["fraction"].Value)), false);
                }
                else if (m.Groups["thousands"].Success)
                {
                    yield return (Format(Integer(m.Groups["thousands"].Value.Replace(",", string.Empty))), false);
                }
                else if (m.Groups["plain"].Success)
                {
                    yield return (Format(decimal.Parse(m.Groups["plain"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)), false);
                }
                else if (m.Groups["glyph"].Success)
                {
                    yield return (Format(Glyphs[m.Groups["glyph"].Value[0]]), false);
                }
                else if (m.Groups["tens"].Success)
                {
                    var value = Words[m.Groups["tens"].Value] + (m.Groups["ones"].Success ? Words[m.Groups["ones"].Value] : 0m);
                    yield return (Format(value), false);
                }
                else if (m.Groups["word"].Success)
                {
                    yield return (Format(Words[m.Groups["word"].Value]), false);
                }
                else if (m.Groups["soft"].Success)
                {
                    yield return (Format(Words[m.Groups["soft"].Value]), true);
                }
            }
        }

        private static decimal Integer(string digits) =>
            decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0m;

        private static decimal Part(string text)
        {
            if (text.Length == 1 && Glyphs.TryGetValue(text[0], out var glyph)) return glyph;
            var slash = text.IndexOf('/');
            if (slash < 0) return 0m;
            var denominator = Integer(text[(slash + 1)..]);
            return denominator == 0 ? 0m : Integer(text[..slash]) / denominator;
        }

        private static string Format(decimal value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

        private static decimal Order(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

        private static List<string> WordsOf(string text) =>
            WordPattern.Matches(Apostrophes.Replace(text.ToLowerInvariant(), string.Empty)).Select(m => m.Value).ToList();

        private static IEnumerable<string> Runs(List<string> words)
        {
            for (var i = 0; i + CopiedRunWords <= words.Count; i++)
            {
                yield return string.Join(" ", words.Skip(i).Take(CopiedRunWords));
            }
        }
    }
}
