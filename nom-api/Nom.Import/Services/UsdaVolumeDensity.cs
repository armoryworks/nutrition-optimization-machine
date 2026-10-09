using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Import.Services
{
    /// <summary>One row of USDA food_portion.csv.</summary>
    public sealed record UsdaPortion(decimal Amount, string? MeasureUnitId, string? Modifier, string? Description, decimal Grams);

    /// <summary>
    /// Derives grams per millilitre from a food's USDA household portions ("1 cup = 125 g").
    /// Foundation names the unit by id; SR Legacy leaves it undetermined and puts it in the
    /// modifier text. A plain measure ("cup") wins over a prepared one ("cup, chopped"), and larger
    /// measures over smaller ones because their gram weights carry less rounding.
    /// </summary>
    public static class UsdaVolumeDensity
    {
        private static readonly Dictionary<string, decimal> MlByUnitId = new()
        {
            ["1000"] = 236.5882m,
            ["1001"] = 14.7868m,
            ["1118"] = 14.7868m,
            ["1002"] = 4.9289m,
            ["1003"] = 1000m,
            ["1004"] = 1m,
            ["1008"] = 473.1765m,
            ["1009"] = 29.5735m,
            ["1045"] = 946.3529m,
        };

        private static readonly (Regex Pattern, decimal Ml)[] MlByName =
        {
            (new Regex(@"^(cups?|c)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 236.5882m),
            (new Regex(@"^(tbsp|tablespoons?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 14.7868m),
            (new Regex(@"^(tsp|teaspoons?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4.9289m),
            (new Regex(@"^fl\.? ?oz\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 29.5735m),
            (new Regex(@"^(quarts?|qt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 946.3529m),
            (new Regex(@"^(pints?|pt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 473.1765m),
            (new Regex(@"^(ml|milliliters?|millilitres?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 1m),
            (new Regex(@"^(l|liters?|litres?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 1000m),
        };

        private static readonly Regex PlainQualifier = new(@"^\s*(\((8 fl oz|1 nlea serving)\))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static decimal? GramsPerMilliliter(IEnumerable<UsdaPortion> portions)
        {
            var best = portions
                .Where(p => p.Amount > 0 && p.Grams > 0)
                .Select(p => (Portion: p, Volume: Volume(p)))
                .Where(x => x.Volume != null)
                .OrderByDescending(x => x.Volume!.Value.Plain)
                .ThenByDescending(x => x.Volume!.Value.Ml)
                .FirstOrDefault();
            if (best.Volume is not { } v) return null;

            var density = best.Portion.Grams / (best.Portion.Amount * v.Ml);
            return density is > 0.05m and < 3m ? Math.Round(density, 4) : null;
        }

        private static (decimal Ml, bool Plain)? Volume(UsdaPortion p)
        {
            var modifier = (p.Modifier ?? string.Empty).Trim();
            if (p.MeasureUnitId != null && MlByUnitId.TryGetValue(p.MeasureUnitId, out var ml))
                return (ml, modifier.Length == 0);

            var text = modifier.Length > 0 ? modifier : (p.Description ?? string.Empty).Trim();
            foreach (var (pattern, unitMl) in MlByName)
            {
                var m = pattern.Match(text);
                if (m.Success)
                    return (unitMl, PlainQualifier.IsMatch(text[m.Length..]));
            }
            return null;
        }
    }
}
