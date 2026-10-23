using System.Globalization;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Splits the amount a shopper types in front of an item ("2 gallons milk", "6 eggs",
    /// "1/2 lb butter") from the item itself, so the catalog never gains a food named after a
    /// quantity. A name is left whole when nothing but a number would remain.
    /// </summary>
    public static class ShoppingItemName
    {
        private static readonly Regex LeadingQuantity = new(
            @"^(?<qty>\d+(?:[.,]\d+)?(?:\s*/\s*\d+)?|\d+\s+\d+/\d+|[½⅓⅔¼¾⅛⅜⅝⅞])\s*(?<unit>(?:gallons?|gal|lbs?|pounds?|oz|ounces?|kg|kilos?|kilograms?|g|grams?|l|liters?|litres?|ml|cups?|dozen|doz|bags?|boxes|box|cans?|jars?|bottles?|packs?|packages?|pkgs?|pkg|bunch(?:es)?|heads?|loaves|loaf|cartons?|pints?|pt|quarts?|qt|rolls?|sticks?|cloves?|ears?|pieces?|pcs?|x)\b\.?)?\s+(?<name>\S.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static (string Name, decimal? Quantity, string? Unit) SplitLeadingQuantity(string typed)
        {
            var text = typed.Trim();
            var match = LeadingQuantity.Match(text);
            if (!match.Success) return (text, null, null);

            var name = match.Groups["name"].Value.Trim();
            if (!Regex.IsMatch(name, @"\p{L}")) return (text, null, null);

            var quantity = ParseQuantity(match.Groups["qty"].Value);
            if (quantity == null) return (text, null, null);

            var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.TrimEnd('.').ToLowerInvariant() : null;
            return (name, quantity, string.IsNullOrEmpty(unit) ? null : unit);
        }

        private static decimal? ParseQuantity(string raw)
        {
            var text = raw.Replace(',', '.').Trim();
            switch (text)
            {
                case "½": return 0.5m;
                case "⅓": return 0.33m;
                case "⅔": return 0.67m;
                case "¼": return 0.25m;
                case "¾": return 0.75m;
                case "⅛": return 0.13m;
                case "⅜": return 0.38m;
                case "⅝": return 0.63m;
                case "⅞": return 0.88m;
            }
            var mixed = Regex.Match(text, @"^(\d+)\s+(\d+)/(\d+)$");
            if (mixed.Success)
                return decimal.Parse(mixed.Groups[1].Value, CultureInfo.InvariantCulture) + Fraction(mixed.Groups[2].Value, mixed.Groups[3].Value);
            var fraction = Regex.Match(text, @"^(\d+)\s*/\s*(\d+)$");
            if (fraction.Success) return Fraction(fraction.Groups[1].Value, fraction.Groups[2].Value);
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private static decimal? Fraction(string numerator, string denominator)
        {
            var d = decimal.Parse(denominator, CultureInfo.InvariantCulture);
            return d == 0 ? null : decimal.Round(decimal.Parse(numerator, CultureInfo.InvariantCulture) / d, 2);
        }
    }
}
