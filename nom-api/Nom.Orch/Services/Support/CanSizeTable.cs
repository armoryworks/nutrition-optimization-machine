using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>One row of the cited can-size table: a numbered can or a standard of fill, the size its source gives, and the source.</summary>
    public sealed record CanSizeRow(
        string Id,
        string Name,
        string? CanNumber,
        string Kind,
        string Era,
        decimal? NetOz,
        decimal? NetOzMax,
        decimal? FluidOz,
        decimal? Cups,
        string Title,
        string Publisher,
        string Year,
        string Page,
        string Locator,
        string Note,
        string? Products = null,
        string? Candidates = null)
    {
        /// <summary>The net weight the row gives; for a range, its middle when the range is no wider than 20 %; null otherwise.</summary>
        public decimal? NetGrams
        {
            get
            {
                if (NetOz is not > 0) return null;
                var oz = NetOz.Value;
                if (NetOzMax is { } max && max > oz)
                {
                    var mid = (oz + max) / 2;
                    if ((max - oz) / mid > CanSizeTable.MaximumSpread) return null;
                    oz = mid;
                }
                return Math.Round(oz * CanSizeTable.GramsPerOunce, 1);
            }
        }

        public decimal? Milliliters => FluidOz is > 0 ? Math.Round(FluidOz.Value * CanSizeTable.MillilitersPerFluidOunce, 1) : null;

        public string SourceRef => "deterministic:can-table:" + Id;

        /// <summary>The numbered cans a period row names for its products ("303|2"); empty for any other row.</summary>
        public IReadOnlyList<string> CandidateNumbers => Candidates == null
            ? Array.Empty<string>()
            : Candidates.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(CanSizeTable.NormalizeNumber).ToList();

        /// <summary>The USDA food categories a period row names ("category:11"); empty for any other row.</summary>
        public IReadOnlyList<int> ProductCategories => Products == null
            ? Array.Empty<int>()
            : Products.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.StartsWith("category:", StringComparison.Ordinal) && int.TryParse(p["category:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : -1)
                .ToList();
    }

    /// <summary>A size a period source names for a line's products, by its numbered can.</summary>
    public sealed record PeriodCandidate(string CanNumber, decimal Grams, string SourceRef);

    /// <summary>
    /// The can-size table (Containers/can-sizes-historic.tsv), transcribed by hand from published
    /// sources, each row citing its title, publisher, year, page and where it can be read: the
    /// numbered cans of the 1930s USDA broadcasts and of today's USDA Food Buying Guide, and the
    /// federal standards of fill for wine and spirits, and period rows that name the candidate cans
    /// of a product class (<see cref="Period"/>). A row without a full citation is refused.
    /// A numbered can is sized by the rows of the recipe's era; rows of one era must agree within
    /// 10 %, and for an unknown era the historic and the modern size must agree.
    /// </summary>
    public static class CanSizeTable
    {
        public const decimal GramsPerOunce = 28.349523125m;
        public const decimal MillilitersPerFluidOunce = 29.5735295625m;
        public const decimal MaximumSpread = 0.20m;
        public const string ResourceName = "Nom.Orch.Containers.can-sizes-historic.tsv";

        private static readonly Lazy<IReadOnlyList<CanSizeRow>> Embedded = new(() => Parse(ReadResource(ResourceName)));

        public static IReadOnlyList<CanSizeRow> Rows => Embedded.Value;

        public static IReadOnlyList<CanSizeRow> Parse(string tsv)
        {
            var rows = new List<CanSizeRow>();
            var lines = tsv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return rows;
            var header = lines[0].Split('\t');
            int Col(string name) => Array.IndexOf(header, name);
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t');
                string Cell(string name) => Col(name) is var i && i >= 0 && i < f.Length ? f[i].Trim() : string.Empty;
                string? Text(string name) => Cell(name) is var v && v.Length > 0 && v != "-" ? v : null;
                decimal? Number(string name) => Text(name) is { } v ? decimal.Parse(v, CultureInfo.InvariantCulture) : null;
                rows.Add(new CanSizeRow(Cell("id"), Cell("name"), Text("canNumber"), Cell("kind"), Cell("era"),
                    Number("netOz"), Number("netOzMax"), Number("fluidOz"), Number("cups"),
                    Cell("title"), Cell("publisher"), Cell("year"), Cell("page"), Cell("locator"), Cell("note"), Text("products"), Text("candidates")));
            }
            return rows;
        }

        /// <summary>Why the table may not ship: a row without its citation, a size or a known era, or a repeated id.</summary>
        public static IReadOnlyList<string> Problems(IReadOnlyList<CanSizeRow> rows)
        {
            var problems = new List<string>();
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Title) || row.Title == "-") problems.Add($"{row.Id}: no title");
                if (string.IsNullOrWhiteSpace(row.Publisher) || row.Publisher == "-") problems.Add($"{row.Id}: no publisher");
                if (!int.TryParse(row.Year, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year < 1800 || year > 2100) problems.Add($"{row.Id}: no year");
                if (string.IsNullOrWhiteSpace(row.Page) || row.Page == "-") problems.Add($"{row.Id}: no page");
                if (string.IsNullOrWhiteSpace(row.Locator) || row.Locator == "-") problems.Add($"{row.Id}: no locator");
                if (string.IsNullOrWhiteSpace(row.Note) || row.Note == "-") problems.Add($"{row.Id}: no quoted text");
                if (row.Era is not (ContainerEras.Historic or ContainerEras.Modern or ContainerEras.Any)) problems.Add($"{row.Id}: era {row.Era}");
                if (!ContainerKinds.All.Contains(row.Kind)) problems.Add($"{row.Id}: kind {row.Kind}");
                if (row.NetOz == null && row.FluidOz == null && row.Cups == null) problems.Add($"{row.Id}: no size");
                if ((row.Candidates == null) != (row.Products == null)) problems.Add($"{row.Id}: a period row names both its products and its candidate cans");
                if (row.Candidates != null)
                {
                    if (row.CandidateNumbers.Count < 2) problems.Add($"{row.Id}: a period row names at least two candidate cans");
                    if (row.ProductCategories.Count == 0 || row.ProductCategories.Any(c => c < 0)) problems.Add($"{row.Id}: products must be category:<USDA category id>");
                    foreach (var number in row.CandidateNumbers.Where(n => Numbered(n, row.Era, rows) == null))
                        problems.Add($"{row.Id}: candidate No. {number} has no size of era {row.Era} in the table");
                }
            }
            problems.AddRange(rows.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: id repeated"));
            return problems;
        }

        /// <summary>A numbered can as the parser reads it ("2 1/2", "2-1/2", "2½") in the table's spelling.</summary>
        public static string NormalizeNumber(string number) =>
            Regex.Replace(number.Trim(), @"\s*[-\s]\s*1/2$|\s+½$", "½");

        /// <summary>The net weight of a numbered can in the recipe's era, with the rows it came from; null when the table gives none or its rows disagree.</summary>
        public static (decimal Grams, string SourceRef)? Numbered(string canNumber, string era, IReadOnlyList<CanSizeRow>? rows = null)
        {
            rows ??= Rows;
            var number = NormalizeNumber(canNumber);
            (decimal, string)? Of(string wanted)
            {
                var sized = rows.Where(r => r.Era == wanted && r.CanNumber == number && r.NetGrams is > 0).ToList();
                if (sized.Count == 0) return null;
                var first = sized[0].NetGrams!.Value;
                return sized.All(r => ContainerSizeAgreement.Agree(r.NetGrams!.Value, first))
                    ? (first, string.Join("+", sized.Select(r => r.SourceRef)))
                    : null;
            }

            return era switch
            {
                ContainerEras.Historic => Of(ContainerEras.Historic),
                ContainerEras.Modern => Of(ContainerEras.Modern),
                _ => Of(ContainerEras.Historic) is { } old && Of(ContainerEras.Modern) is { } now && ContainerSizeAgreement.Agree(old.Item1, now.Item1)
                    ? (now.Item1, now.Item2 + "+" + old.Item2)
                    : null,
            };
        }

        /// <summary>
        /// The sizes a period source names for an unsized container of a food in an old or undated
        /// book: the candidate numbered cans of the historic row whose kind is the line's and whose
        /// products hold the food's USDA category, each sized by the historic rows of the table.
        /// Empty when no period row names the food, or the line names a size word.
        /// </summary>
        public static IReadOnlyList<PeriodCandidate> Period(string kind, string sizeWord, int? usdaCategoryId, IReadOnlyList<CanSizeRow>? rows = null)
        {
            rows ??= Rows;
            if (usdaCategoryId == null || sizeWord.Length > 0) return Array.Empty<PeriodCandidate>();
            var row = rows.FirstOrDefault(r => r.Era == ContainerEras.Historic && r.Kind == kind && r.Candidates != null && r.ProductCategories.Contains(usdaCategoryId.Value));
            if (row == null) return Array.Empty<PeriodCandidate>();
            var sized = row.CandidateNumbers
                .Select(n => Numbered(n, ContainerEras.Historic, rows) is { } size ? new PeriodCandidate(n, size.Grams, row.SourceRef + "+" + size.SourceRef) : null)
                .ToList();
            return sized.Any(c => c == null) ? Array.Empty<PeriodCandidate>() : sized.Select(c => c!).ToList();
        }

        /// <summary>The standard-of-fill row for a wine or a spirit bottle; null for any other food.</summary>
        public static CanSizeRow? Fill(string? usdaDescription, IReadOnlyList<CanSizeRow>? rows = null)
        {
            rows ??= Rows;
            if (string.IsNullOrWhiteSpace(usdaDescription)) return null;
            var d = usdaDescription.ToLowerInvariant();
            if (!d.StartsWith("alcoholic beverage", StringComparison.Ordinal)) return null;
            var id = Regex.IsMatch(d, @"\bwine\b") && !Regex.IsMatch(d, @"\bcooking\b") ? "fill-wine"
                : Regex.IsMatch(d, @"\bdistilled\b") ? "fill-spirits"
                : null;
            return id == null ? null : rows.FirstOrDefault(r => r.Id == id);
        }

        internal static string ReadResource(string name)
        {
            using var stream = typeof(CanSizeTable).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource {name} is missing from Nom.Orch.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
