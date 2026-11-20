using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Nom.Data.Nutrition;

namespace Nom.Import.Services
{
    /// <summary>
    /// Reads the USDA files beside food.csv that the catalog admission rule needs: the category
    /// names (food_category.csv) and each food's own calorie factors
    /// (food_nutrient_conversion_factor.csv joined to food_calorie_conversion_factor.csv). A file
    /// that is not there gives an empty result, so a food is then judged by the general formula.
    /// </summary>
    public static class UsdaFoodFacts
    {
        public static Dictionary<int, string> Categories(string? categoryCsv)
        {
            var result = new Dictionary<int, string>();
            if (categoryCsv == null || !File.Exists(categoryCsv)) return result;
            using var reader = new StreamReader(categoryCsv);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length >= 3 && int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) result[id] = f[2];
            }
            return result;
        }

        /// <summary>
        /// The calorie factors of each wanted food. A factor row with a missing or non-positive
        /// value is passed over (USDA ships 0/0/0 rows for some foods), and of several usable rows
        /// for one food the first in the file wins.
        /// </summary>
        public static Dictionary<string, CalorieFactors> CalorieFactors(string? conversionCsv, string? calorieCsv, ISet<string> fdcIds)
        {
            var result = new Dictionary<string, CalorieFactors>();
            if (conversionCsv == null || calorieCsv == null || !File.Exists(conversionCsv) || !File.Exists(calorieCsv)) return result;

            var foodOf = new Dictionary<string, string>();
            using (var reader = new StreamReader(conversionCsv))
            {
                reader.ReadLine();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length >= 2 && fdcIds.Contains(f[1])) foodOf[f[0]] = f[1];
                }
            }

            using (var reader = new StreamReader(calorieCsv))
            {
                reader.ReadLine();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var f = CsvLine.Split(line);
                    if (f.Length < 4 || !foodOf.TryGetValue(f[0], out var fdcId) || result.ContainsKey(fdcId)) continue;
                    if (Nom.Data.Nutrition.CalorieFactors.Of(Number(f[1]), Number(f[2]), Number(f[3])) is { } factors) result[fdcId] = factors;
                }
            }
            return result;
        }

        private static decimal? Number(string value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
