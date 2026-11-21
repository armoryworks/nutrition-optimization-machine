using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nom.Import.Services
{
    /// <summary>
    /// The ingredient statement USDA FDC publishes for each branded food (the <c>ingredients</c> column of
    /// <c>branded_food.csv</c>), streamed for the FDC ids asked for. FDC branded data is CC0; the statement is stored as
    /// evidence of a prepared meal's contents under source <c>fdc:{fdcId}</c>.
    /// </summary>
    public static class FdcIngredientStatements
    {
        /// <summary>The statement of each asked-for FDC id that publishes one.</summary>
        public static Dictionary<string, string> Read(string csvDir, IReadOnlySet<string> fdcIds)
        {
            var path = Path.Combine(csvDir, "branded_food.csv");
            if (!File.Exists(path))
                path = Directory.GetFiles(csvDir, "branded_food.csv", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new FileNotFoundException($"branded_food.csv not found under {csvDir}");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var reader = new StreamReader(path);
            var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
            var iFdc = Array.FindIndex(header, h => h.Equals("fdc_id", StringComparison.OrdinalIgnoreCase));
            var iIngredients = Array.FindIndex(header, h => h.Equals("ingredients", StringComparison.OrdinalIgnoreCase));
            if (iFdc < 0 || iIngredients < 0) throw new InvalidDataException("branded_food.csv has no fdc_id or ingredients column");
            string? line;
            while ((line = reader.ReadLine()) != null && result.Count < fdcIds.Count)
            {
                var f = CsvLine.Split(line);
                if (f.Length <= Math.Max(iFdc, iIngredients) || !fdcIds.Contains(f[iFdc])) continue;
                var statement = f[iIngredients].Trim();
                if (statement.Length > 0) result[f[iFdc]] = statement;
            }
            return result;
        }
    }
}
