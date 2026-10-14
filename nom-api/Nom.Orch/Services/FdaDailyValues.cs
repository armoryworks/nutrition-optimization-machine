using System;
using System.Collections.Generic;

namespace Nom.Orch.Services
{
    /// <summary>
    /// FDA Daily Values for adults and children 4+ (21 CFR 101.9(c), 2016 rule, required on labels
    /// since 2020), for the nutrients the label shows with a % Daily Value. Trans fat, total
    /// sugars and protein carry no %DV on a standard label, so they are absent.
    /// </summary>
    public static class FdaDailyValues
    {
        private static readonly Dictionary<string, (decimal Amount, string Unit)> ByName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["fat"] = (78m, "g"),
            ["total fat"] = (78m, "g"),
            ["total lipid (fat)"] = (78m, "g"),
            ["saturated fat"] = (20m, "g"),
            ["fatty acids, total saturated"] = (20m, "g"),
            ["cholesterol"] = (300m, "mg"),
            ["sodium"] = (2300m, "mg"),
            ["sodium, na"] = (2300m, "mg"),
            ["total carbohydrates"] = (275m, "g"),
            ["total carbohydrate"] = (275m, "g"),
            ["carbohydrates"] = (275m, "g"),
            ["carbohydrate, by difference"] = (275m, "g"),
            ["dietary fiber"] = (28m, "g"),
            ["fiber, total dietary"] = (28m, "g"),
            ["added sugars"] = (50m, "g"),
            ["sugars, added"] = (50m, "g"),
            ["vitamin d"] = (20m, "µg"),
            ["vitamin d (d2 + d3)"] = (20m, "µg"),
            ["calcium"] = (1300m, "mg"),
            ["calcium, ca"] = (1300m, "mg"),
            ["iron"] = (18m, "mg"),
            ["iron, fe"] = (18m, "mg"),
            ["potassium"] = (4700m, "mg"),
            ["potassium, k"] = (4700m, "mg"),
        };

        private static readonly Dictionary<string, decimal> MilligramsPer = new(StringComparer.OrdinalIgnoreCase)
        {
            ["g"] = 1000m,
            ["mg"] = 1m,
            ["µg"] = 0.001m,
            ["μg"] = 0.001m,
            ["ug"] = 0.001m,
            ["mcg"] = 0.001m,
        };

        /// <summary>
        /// The whole-number percent of the Daily Value that <paramref name="amount"/> of the named
        /// nutrient supplies, or null when the nutrient has no Daily Value or the unit is unknown.
        /// </summary>
        public static decimal? PercentOf(string? nutrientName, decimal amount, string? unit)
        {
            if (nutrientName == null || !ByName.TryGetValue(nutrientName.Trim(), out var dv)) return null;
            if (string.IsNullOrWhiteSpace(unit) || !MilligramsPer.TryGetValue(unit.Trim(), out var from)) return null;
            var inDvUnit = amount * from / MilligramsPer[dv.Unit];
            return Math.Round(inDvUnit / dv.Amount * 100m, 0, MidpointRounding.AwayFromZero);
        }
    }
}
