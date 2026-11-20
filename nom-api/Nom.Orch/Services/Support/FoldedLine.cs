using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Reads a recipe line that an import or a food merge joined from several source lines
    /// ("1 3/4 cup sugar + 1/3 cup sugar") back into those lines. The same " + " also stands inside
    /// a single line ("1 cup + 2 Tablespoons sugar", "1 large egg + 1 egg white"), so the recipe's
    /// own source list decides where one is kept: the line is read only as whole lines of that
    /// list. A recipe that has no stored source (null) gives every part as a line, unless a part
    /// is only an amount, which leaves the whole line unread. Text only: no database and no model.
    /// </summary>
    public static class FoldedLine
    {
        public const string Separator = " + ";

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BareAmount = new(
            @"^[\d\s/.,¼½¾⅓⅔⅛⅜⅝⅞~-]*(?:cups?|c|tablespoons?|teaspoons?|tbsps?|tsps?|tbs|t|ounces?|oz|pounds?|lbs?|grams?|g|ml|pinch(?:es)?|dash(?:es)?|large|small|medium)?\.?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Key(string? line) =>
            Whitespace.Replace(WebUtility.HtmlDecode(line ?? string.Empty), " ").Trim().ToLowerInvariant();

        public static IReadOnlySet<string> SourceLines(string? rawJsonLd)
        {
            var lines = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(rawJsonLd)) return lines;

            try
            {
                using var document = JsonDocument.Parse(rawJsonLd);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("recipeIngredient", out var list)
                    || list.ValueKind != JsonValueKind.Array) return lines;

                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && Key(item.GetString()) is { Length: > 0 } key) lines.Add(key);
                }

                return lines;
            }
            catch (JsonException)
            {
                return lines;
            }
        }

        public static IReadOnlyList<string>? Segments(string? rawLine, IReadOnlySet<string>? sourceLines)
        {
            var line = rawLine ?? string.Empty;
            var parts = Parts(line);
            if (sourceLines != null) return Partition(line, parts, sourceLines);

            var texts = parts.Select(p => line[p.Start..p.End]).ToList();
            return texts.Any(IsBareAmount) ? null : texts;
        }

        private static bool IsBareAmount(string part) =>
            !part.Any(char.IsLetter) || BareAmount.IsMatch(WebUtility.HtmlDecode(part).Trim());

        private static List<(int Start, int End)> Parts(string line)
        {
            var parts = new List<(int Start, int End)>();
            var from = 0;
            while (true)
            {
                var at = line.IndexOf(Separator, from, StringComparison.Ordinal);
                var start = from;
                var end = at < 0 ? line.Length : at;
                while (start < end && char.IsWhiteSpace(line[start])) start++;
                while (end > start && char.IsWhiteSpace(line[end - 1])) end--;
                parts.Add((start, end));
                if (at < 0) return parts;
                from = at + Separator.Length;
            }
        }

        private static List<string>? Partition(string line, List<(int Start, int End)> parts, IReadOnlySet<string> sourceLines)
        {
            var count = parts.Count;
            var fewest = new int[count + 1];
            var cut = new int[count + 1];
            Array.Fill(fewest, int.MaxValue);
            fewest[0] = 0;
            for (var end = 1; end <= count; end++)
            {
                for (var start = 0; start < end; start++)
                {
                    if (fewest[start] == int.MaxValue || fewest[start] + 1 >= fewest[end]) continue;
                    if (!sourceLines.Contains(Key(line[parts[start].Start..parts[end - 1].End]))) continue;
                    fewest[end] = fewest[start] + 1;
                    cut[end] = start;
                }
            }

            if (fewest[count] == int.MaxValue) return null;
            var segments = new List<string>();
            for (var end = count; end > 0; end = cut[end]) segments.Insert(0, line[parts[cut[end]].Start..parts[end - 1].End]);
            return segments;
        }
    }
}
