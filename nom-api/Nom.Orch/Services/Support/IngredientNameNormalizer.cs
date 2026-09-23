using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Deterministic cleanup of scraped ingredient names before they become (or
    /// are matched against) catalog rows: strips leading quantities and units,
    /// parenthetical prep notes and stray punctuation. The recipe's own
    /// RecipeIngredient.RawLine keeps the original text, so nothing is lost.
    /// Pure string rules — no model, no network (see audit N-68/N-79).
    /// </summary>
    public static partial class IngredientNameNormalizer
    {
        private const string Fractions = "¼½¾⅓⅔⅛⅜⅝⅞";

        // "1", "1.5", "1,5", "1/2", "1 1/2", "½", "1½", and ranges "1-2" / "1 to 2".
        private static readonly string Number =
            $@"(?:\d+(?:[.,]\d+)?(?:\s*/\s*\d+)?(?:\s+\d+\s*/\s*\d+)?[{Fractions}]?|[{Fractions}])";

        private static readonly Regex LeadingQuantity = new(
            $@"^\s*(?:{Number}(?:\s*(?:-|–|—|to)\s*{Number})?|(?:an?|one|two|three|four|five|six|seven|eight|nine|ten|twelve|half|halves|quarter))(?=[\s.])[\s.]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LeadingUnit = new(
            @"^\s*(?:small|medium|large|extra[- ]large|heaping|scant|level|generous|rounded)?\s*" +
            @"(?:pints?|quarts?|gallons?|cups?|cans?|jars?|packages?|pkgs?|packets?|boxes?|bags?|bunch(?:es)?|" +
            @"slices?|pounds?|lbs?\.?|ounces?|oz\.?|fl\.?\s*oz\.?|tablespoons?|tbsps?\.?|teaspoons?|tsps?\.?|" +
            @"cloves?|sticks?|pieces?|pinch(?:es)?|dash(?:es)?|handfuls?|sprigs?|heads?|stalks?|ribs?|ears?|" +
            @"fillets?|filets?|strips?|cubes?|knobs?|pats?|liters?|litres?|l\b|ml\b|milliliters?|grams?|g\b|kgs?|kilograms?)" +
            @"\b\.?\s*(?:of\s+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Parenthetical = new(
            @"\s*\([^()]*\)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Whitespace = new(
            @"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name?.Trim() ?? string.Empty;

            var s = WebUtility.HtmlDecode(name).Trim();

            for (var pass = 0; pass < 3; pass++)
            {
                s = Parenthetical.Replace(s, string.Empty);
            }

            // "1 (15 oz) can black beans": quantity, unit, quantity again — loop
            // until neither rule bites.
            string before;
            do
            {
                before = s;
                s = LeadingQuantity.Replace(s, string.Empty);
                s = LeadingUnit.Replace(s, string.Empty);
            } while (s != before && s.Length > 0);

            s = Whitespace.Replace(s, " ").Trim().Trim(',', ';', ':', '-', '–', '.', ' ');

            // Over-stripping ("2 cups" alone, "half") must not mint an empty or
            // one-letter name — keep the original in that case.
            return s.Length >= 2 ? s : name.Trim();
        }

        /// <summary>True when normalization would change this catalog name.</summary>
        public static bool IsResidue(string name) =>
            !string.Equals(Normalize(name), name?.Trim(), StringComparison.Ordinal);
    }
}
