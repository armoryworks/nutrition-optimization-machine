using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// One class of Containers/container-classes.tsv: which catalog foods it holds (a USDA category
    /// and words of the USDA description), which branded products count toward its mode (branded
    /// categories, words every product must hold and words none may hold), the container kind and
    /// whether the mode is read in grams or millilitres.
    /// </summary>
    public sealed record ContainerClass(
        string Id,
        string Kind,
        string Basis,
        IReadOnlySet<int> UsdaCategories,
        string DescriptionAll,
        string DescriptionNone,
        IReadOnlySet<string> BrandedCategories,
        string ProductAll,
        string ProductNone,
        string Note)
    {
        public bool ByVolume => Basis == "ml";

        public bool HoldsFood(int? usdaCategoryId, string? usdaDescription)
        {
            if (usdaCategoryId is not { } category || !UsdaCategories.Contains(category)) return false;
            var words = ContainerWords.Of(usdaDescription);
            return ContainerWords.HasAll(DescriptionAll, words) && !ContainerWords.HasAny(DescriptionNone, words);
        }

        public bool HoldsProduct(string? brandedCategory, ISet<string> descriptionWords) =>
            brandedCategory != null && BrandedCategories.Contains(brandedCategory)
            && ContainerWords.HasAll(ProductAll, descriptionWords) && !ContainerWords.HasAny(ProductNone, descriptionWords);
    }

    /// <summary>
    /// The class map that ties a catalog food to its branded products, set by rule from a USDA
    /// category and description words, never from a product name. A food in no class gets no
    /// branded size, and a food two classes give a size of one kind gets none.
    /// </summary>
    public static class ContainerClasses
    {
        public const string ResourceName = "Nom.Orch.Containers.container-classes.tsv";

        private static readonly Lazy<IReadOnlyList<ContainerClass>> Embedded = new(() => Parse(CanSizeTable.ReadResource(ResourceName)));

        public static IReadOnlyList<ContainerClass> All => Embedded.Value;

        public static IReadOnlyList<ContainerClass> Parse(string tsv)
        {
            var classes = new List<ContainerClass>();
            var lines = tsv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return classes;
            var header = lines[0].Split('\t');
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t');
                string Cell(string name) => Array.IndexOf(header, name) is var i && i >= 0 && i < f.Length ? f[i].Trim() : string.Empty;
                classes.Add(new ContainerClass(
                    Cell("id"), Cell("kind"), Cell("basis"),
                    Cell("usdaCategories").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToHashSet(),
                    Cell("descriptionAll"), Cell("descriptionNone"),
                    Cell("brandedCategories").Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal),
                    Cell("productAll"), Cell("productNone"), Cell("note")));
            }
            return classes;
        }

        /// <summary>Why the class map may not ship: an unknown kind or basis, no category, no product words, or a repeated id.</summary>
        public static IReadOnlyList<string> Problems(IReadOnlyList<ContainerClass> classes)
        {
            var problems = new List<string>();
            foreach (var c in classes)
            {
                if (!ContainerKinds.All.Contains(c.Kind)) problems.Add($"{c.Id}: kind {c.Kind}");
                if (c.Basis is not ("g" or "ml")) problems.Add($"{c.Id}: basis {c.Basis}");
                if (c.UsdaCategories.Count == 0) problems.Add($"{c.Id}: no USDA category");
                if (c.BrandedCategories.Count == 0) problems.Add($"{c.Id}: no branded category");
                if (c.DescriptionAll is "" or "-") problems.Add($"{c.Id}: no description words");
                if (c.ProductAll is "" or "-") problems.Add($"{c.Id}: no product words");
            }
            problems.AddRange(classes.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: id repeated"));
            return problems;
        }

        public static IReadOnlyList<ContainerClass> For(int? usdaCategoryId, string? usdaDescription, IReadOnlyList<ContainerClass>? classes = null) =>
            (classes ?? All).Where(c => c.HoldsFood(usdaCategoryId, usdaDescription)).ToList();
    }
}
