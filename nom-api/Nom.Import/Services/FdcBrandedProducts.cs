using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nom.Orch.Services.Support;

namespace Nom.Import.Services
{
    /// <summary>
    /// Every US branded food of a FoodData Central branded release that is on sale and publishes an ingredient statement
    /// (<c>branded_food.csv</c> with the descriptions of <c>food.csv</c>), one per product: a product listed under several
    /// FDC ids (the same GTIN or UPC) counts once, as its latest listing. FDC branded data is CC0; it is read here as the
    /// makers' label statements for the branded consensus (<see cref="BrandedConsensus"/>), never as nutrition numbers.
    /// </summary>
    public static class FdcBrandedProducts
    {
        private static string FileUnder(string csvDir, string name)
        {
            var path = Path.Combine(csvDir, name);
            return File.Exists(path)
                ? path
                : Directory.GetFiles(csvDir, name, SearchOption.AllDirectories).FirstOrDefault() ?? throw new FileNotFoundException($"{name} not found under {csvDir}");
        }

        /// <summary>The products, in FDC id order.</summary>
        public static List<ConsensusProduct> Read(string csvDir)
        {
            var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var reader = new StreamReader(FileUnder(csvDir, "food.csv")))
            {
                var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
                int Col(string n) => Array.FindIndex(header, h => h.Equals(n, StringComparison.OrdinalIgnoreCase));
                int iFdc = Col("fdc_id"), iType = Col("data_type"), iDesc = Col("description");
                if (iFdc < 0 || iType < 0 || iDesc < 0) throw new InvalidDataException("food.csv has no fdc_id, data_type or description column");
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length > Math.Max(iFdc, Math.Max(iType, iDesc)) && f[iType] == "branded_food") descriptions[f[iFdc]] = f[iDesc];
                }
            }

            var best = new Dictionary<string, (ConsensusProduct Product, string Date, long Id)>(StringComparer.Ordinal);
            using (var reader = new StreamReader(FileUnder(csvDir, "branded_food.csv")))
            {
                var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
                int Col(string n) => Array.FindIndex(header, h => h.Equals(n, StringComparison.OrdinalIgnoreCase));
                int iFdc = Col("fdc_id"), iGtin = Col("gtin_upc"), iIng = Col("ingredients"), iCat = Col("branded_food_category"),
                    iCountry = Col("market_country"), iGone = Col("discontinued_date"), iDate = Col("available_date");
                if (new[] { iFdc, iGtin, iIng, iCat, iCountry, iGone, iDate }.Any(i => i < 0)) throw new InvalidDataException("branded_food.csv lacks a column the consensus reads");
                var last = new[] { iFdc, iGtin, iIng, iCat, iCountry, iGone, iDate }.Max();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length <= last) continue;
                    if (f[iCountry] is not ("United States" or "") || f[iGone].Length > 0) continue;
                    var statement = f[iIng].Trim();
                    if (statement.Length == 0 || !long.TryParse(f[iFdc], out var id)) continue;
                    var key = f[iGtin].TrimStart('0');
                    if (key.Length == 0) key = "fdc:" + f[iFdc];
                    var product = new ConsensusProduct(f[iFdc], descriptions.GetValueOrDefault(f[iFdc]) ?? string.Empty, f[iCat].Length == 0 ? null : f[iCat], statement);
                    if (!best.TryGetValue(key, out var cur) || string.CompareOrdinal(f[iDate], cur.Date) > 0 || (f[iDate] == cur.Date && id > cur.Id))
                        best[key] = (product, f[iDate], id);
                }
            }
            return best.Values.OrderBy(v => v.Id).Select(v => v.Product).ToList();
        }
    }
}
