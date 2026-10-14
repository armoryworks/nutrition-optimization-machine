using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Nom.Import.Services
{
    /// <summary>
    /// Reads USDA food_portion.csv rows per food, naming each unit from measure_unit.csv when it
    /// sits beside the portion file (Foundation names units by id; SR Legacy leaves them undetermined).
    /// </summary>
    public static class UsdaPortionFile
    {
        public static Dictionary<string, List<UsdaPortion>> Read(string? portionCsv, ISet<string>? fdcIds = null)
        {
            var byFood = new Dictionary<string, List<UsdaPortion>>();
            if (portionCsv == null || !File.Exists(portionCsv)) return byFood;
            var unitNames = ReadUnitNames(Path.Combine(Path.GetDirectoryName(portionCsv) ?? string.Empty, "measure_unit.csv"));

            using var reader = new StreamReader(portionCsv);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length < 8 || (fdcIds != null && !fdcIds.Contains(f[1]))) continue;
                if (!decimal.TryParse(f[7], CultureInfo.InvariantCulture, out var grams)
                    || !decimal.TryParse(f[3], CultureInfo.InvariantCulture, out var amount)) continue;
                if (!byFood.TryGetValue(f[1], out var list)) byFood[f[1]] = list = new List<UsdaPortion>();
                list.Add(new UsdaPortion(amount, f[4], f[6], f[5], grams, unitNames.GetValueOrDefault(f[4])));
            }
            return byFood;
        }

        private static Dictionary<string, string> ReadUnitNames(string path)
        {
            var names = new Dictionary<string, string>();
            if (!File.Exists(path)) return names;
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length >= 2) names[f[0]] = f[1];
            }
            return names;
        }
    }
}
