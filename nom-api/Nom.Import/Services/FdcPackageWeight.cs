using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Import.Services
{
    /// <summary>A package's stated net contents: by weight, by volume, or both.</summary>
    public sealed record PackageSize(decimal? Grams, decimal? Milliliters);

    /// <summary>
    /// Reads FDC Branded Foods' free-text package_weight ("15 oz/425 g", "16 fl oz/1 PT/473 mL",
    /// "10 LBR", "12 OZA"). The string is split at "/"; a gram or millilitre segment wins over an
    /// ounce, pound, pint, quart or gallon one, since it carries less rounding. GS1 unit codes are
    /// read (ONZ ounce, OZA fluid ounce, LBR pound, GRM gram, MLT millilitre, LTR litre, GLL gallon).
    /// A multi-pack ("4 x 5.3 oz", "12 ct", "6 pk"), a count ("1 EA") and anything unread give null;
    /// a multi-pack is refused, never divided.
    /// </summary>
    public static class FdcPackageWeight
    {
        private const decimal Ounce = 28.349523125m;
        private const decimal Pound = 453.59237m;
        private const decimal FluidOunce = 29.5735295625m;

        private static readonly Regex Multi = new(
            @"(\d\s*[x×]\s*\d|\bct\b|\bcount\b|\bpk\b|\bpack\b|\bpacks\b|\bservings?\b|\bea\b|\beach\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Segment = new(@"^(?<n>\d+(?:\.\d+)?|\.\d+)\s*(?<u>.+?)\.?$", RegexOptions.Compiled);

        private static readonly (Regex Unit, bool Mass, decimal Factor, bool Exact)[] Units =
        {
            (new Regex(@"^(fl\.?\s*oz\.?|floz|oza)$", RegexOptions.IgnoreCase), false, FluidOunce, false),
            (new Regex(@"^(oz\.?|onz|nz|ounces?)$", RegexOptions.IgnoreCase), true, Ounce, false),
            (new Regex(@"^(lbs?\.?|lbr|pounds?)$", RegexOptions.IgnoreCase), true, Pound, false),
            (new Regex(@"^(kg|kgm|kilograms?)$", RegexOptions.IgnoreCase), true, 1000m, true),
            (new Regex(@"^(g|grm|grams?|gr)$", RegexOptions.IgnoreCase), true, 1m, true),
            (new Regex(@"^(ml|mlt|milliliters?)$", RegexOptions.IgnoreCase), false, 1m, true),
            (new Regex(@"^(l|ltr|liters?|litres?)$", RegexOptions.IgnoreCase), false, 1000m, true),
            (new Regex(@"^(pt|pints?)$", RegexOptions.IgnoreCase), false, 473.176473m, false),
            (new Regex(@"^(qt|qts|quarts?)$", RegexOptions.IgnoreCase), false, 946.352946m, false),
            (new Regex(@"^(gal|gll|gallons?)$", RegexOptions.IgnoreCase), false, 3785.411784m, false),
        };

        public static PackageSize? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || Multi.IsMatch(text)) return null;
            decimal? grams = null, ml = null;
            bool gramsExact = false, mlExact = false;
            foreach (var part in text.Split('/').Select(p => p.Trim().ToLowerInvariant()))
            {
                var m = Segment.Match(part);
                if (!m.Success || !decimal.TryParse(m.Groups["n"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0) continue;
                var unit = m.Groups["u"].Value.Trim();
                foreach (var (pattern, mass, factor, exact) in Units)
                {
                    if (!pattern.IsMatch(unit)) continue;
                    if (mass && (grams == null || (exact && !gramsExact)))
                    {
                        grams = value * factor;
                        gramsExact = exact;
                    }
                    if (!mass && (ml == null || (exact && !mlExact)))
                    {
                        ml = value * factor;
                        mlExact = exact;
                    }
                    break;
                }
            }
            if (grams == null && ml == null) return null;
            return new PackageSize(grams is { } g ? Math.Round(g, 1) : null, ml is { } v ? Math.Round(v, 1) : null);
        }
    }
}
