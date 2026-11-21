using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nom.Orch.Services.Support;

namespace Nom.Import.Services
{
    /// <summary>What the branded scan read: the package sizes of each class's products, and how many products it passed over and why.</summary>
    public sealed class BrandedContainerSizes
    {
        public Dictionary<string, Dictionary<int, int>> SizesByClass { get; } = new(StringComparer.Ordinal);
        public int Products { get; set; }
        public int Deduplicated { get; set; }
        public Dictionary<string, int> SkippedByReason { get; } = new(StringComparer.Ordinal);

        public void Skip(string reason) => SkippedByReason[reason] = SkippedByReason.GetValueOrDefault(reason) + 1;
    }

    /// <summary>
    /// Streams FDC Branded Foods (branded_food.csv and food.csv) once for every class of the
    /// container class map: products of the classes' branded categories, United States, not
    /// discontinued, one per GTIN (the latest available_date), whose description holds the class's
    /// words and none of its excluded words, with a package weight that reads and is not a
    /// multi-pack. A size is kept in whole grams, or whole millilitres for a class read by volume.
    /// Only a weight is taken from branded data, never a nutrient value.
    /// </summary>
    public static class BrandedContainerScan
    {
        public static BrandedContainerSizes Scan(string brandedDir, IReadOnlyList<ContainerClass> classes)
        {
            var result = new BrandedContainerSizes();
            var byCategory = new Dictionary<string, List<ContainerClass>>(StringComparer.Ordinal);
            foreach (var c in classes)
                foreach (var category in c.BrandedCategories)
                {
                    if (!byCategory.TryGetValue(category, out var list)) byCategory[category] = list = new List<ContainerClass>();
                    list.Add(c);
                }

            var products = new Dictionary<string, (string Gtin, string Category, string Weight, string Available)>(StringComparer.Ordinal);
            using (var reader = new StreamReader(Path.Combine(brandedDir, "branded_food.csv")))
            {
                var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
                int Col(string name) => Array.IndexOf(header, name);
                int id = Col("fdc_id"), gtin = Col("gtin_upc"), category = Col("branded_food_category"), weight = Col("package_weight"),
                    available = Col("available_date"), country = Col("market_country"), discontinued = Col("discontinued_date");
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length <= Math.Max(discontinued, Math.Max(country, weight)) || !byCategory.ContainsKey(f[category])) continue;
                    if (f[country] != "United States") { result.Skip("not US"); continue; }
                    if (f[discontinued].Length > 0) { result.Skip("discontinued"); continue; }
                    products[f[id]] = (f[gtin], f[category], f[weight], f[available]);
                }
            }

            var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var reader = new StreamReader(Path.Combine(brandedDir, "food.csv")))
            {
                var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
                int id = Array.IndexOf(header, "fdc_id"), description = Array.IndexOf(header, "description");
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length > description && products.ContainsKey(f[id])) descriptions[f[id]] = f[description];
                }
            }

            var latest = new Dictionary<string, (string FdcId, string Available)>(StringComparer.Ordinal);
            foreach (var (fdcId, p) in products)
            {
                var key = p.Gtin.TrimStart('0') is { Length: > 0 } g ? g : fdcId;
                if (!latest.TryGetValue(key, out var held) || Compare(p.Available, fdcId, held.Available, held.FdcId) > 0)
                    latest[key] = (fdcId, p.Available);
            }
            result.Products = products.Count;
            result.Deduplicated = latest.Count;

            foreach (var (fdcId, _) in latest.Values)
            {
                var p = products[fdcId];
                var size = FdcPackageWeight.Parse(p.Weight);
                if (size == null) { result.Skip(string.IsNullOrWhiteSpace(p.Weight) ? "no package weight" : "package weight unread or a multi-pack"); continue; }
                var words = ContainerWords.Of(descriptions.GetValueOrDefault(fdcId));
                foreach (var c in byCategory[p.Category])
                {
                    if (!c.HoldsProduct(p.Category, words)) continue;
                    var value = c.ByVolume ? size.Milliliters : size.Grams;
                    if (value is not > 0) { result.Skip(c.ByVolume ? "no volume for a class read by volume" : "no weight for a class read by weight"); continue; }
                    if (!result.SizesByClass.TryGetValue(c.Id, out var sizes)) result.SizesByClass[c.Id] = sizes = new Dictionary<int, int>();
                    var whole = (int)Math.Round(value.Value, MidpointRounding.ToEven);
                    sizes[whole] = sizes.GetValueOrDefault(whole) + 1;
                }
            }
            return result;
        }

        private static int Compare(string availableA, string idA, string availableB, string idB)
        {
            var byDate = string.CompareOrdinal(availableA, availableB);
            if (byDate != 0) return byDate;
            return long.TryParse(idA, out var a) && long.TryParse(idB, out var b) ? a.CompareTo(b) : string.CompareOrdinal(idA, idB);
        }
    }
}
