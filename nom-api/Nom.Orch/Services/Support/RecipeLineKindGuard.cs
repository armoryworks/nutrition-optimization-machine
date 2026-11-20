using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Deterministic veto on a label that takes a recipe line out of the nutrition label, the
    /// missing-amount count and the curated-ingredient gate. A line that states an amount, or whose
    /// wording is exactly the name of a curated food, is an ingredient whatever its label says. The
    /// wider rule, a short line that sits on a curated food, is applied only when asked for. A line
    /// that reads as a heading or a note is never vetoed: a label before a colon not followed by an
    /// amount ("Piping tips: Wilton #125"), a line in capitals ("MEATBALLS"), a line that starts with
    /// "or" ("or 6 baked potatoes") and fat or liquid taken from the dish itself ("dripping from the
    /// top of the soup").
    /// </summary>
    public static class RecipeLineKindGuard
    {
        public const int ShortLineLength = 40;
        public const string QuantityReason = "states an amount";
        public const string CuratedNameReason = "names a curated food";
        public const string ShortLineReason = "short line on a curated food";

        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex ColonLabel = new(
            @"^\s*\p{L}[^:\d]{0,40}:(?!\s*(?:[\d¼½¾⅓⅔⅛⅜⅝⅞]|(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|twelve|half)\b))", Options);

        private static readonly Regex LeadingOr = new(@"^\s*or\b", Options);

        private static readonly Regex FromTheDish = new(
            @"\bfrom\s+the\s+(?:top\s+of\s+the\s+)?(?:soup(?:-stock)?|stock|stock-?pot|pot|pan|roast|joint|meat|dish|broth|liquor|gravy|boiling|bones?)\b", Options);

        /// <summary>Whether a line reads as a heading or a note rather than an ingredient, so no veto applies to it.</summary>
        public static bool ReadsAsHeading(string? rawLine)
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0) return false;
            var letters = line.Where(char.IsLetter).ToList();
            var capitals = letters.Count >= 3 && letters.All(char.IsUpper) && !line.Any(char.IsDigit);
            return capitals || ColonLabel.IsMatch(line) || LeadingOr.IsMatch(line) || FromTheDish.IsMatch(line);
        }

        /// <summary>
        /// Why <paramref name="kind"/> may not stand on this line, or null when it may. Only a kind
        /// that is not an ingredient is ever refused.
        /// </summary>
        public static string? Refusal(string? kind, decimal quantity, string? rawLine, bool onCuratedFood, IReadOnlySet<string> curatedNames, bool shortLinesOnCuratedFoods = false)
        {
            if (!RecipeIngredientLineKind.IsExempt(kind)) return null;
            if (ReadsAsHeading(rawLine)) return null;
            if (quantity > 0) return QuantityReason;
            if (NamesCuratedFood(rawLine, curatedNames)) return CuratedNameReason;
            if (shortLinesOnCuratedFoods && onCuratedFood && (rawLine ?? string.Empty).Trim().Length <= ShortLineLength) return ShortLineReason;
            return null;
        }

        public static bool NamesCuratedFood(string? rawLine, IReadOnlySet<string> curatedNames)
        {
            var wording = IngredientNameNormalizer.Normalize(rawLine ?? string.Empty);
            return wording.Length > 0 && IngredientNameNormalizer.ExactKeys(wording).Any(curatedNames.Contains);
        }

        /// <summary>The names of the curated foods that have nutrition, the map line relinking matches wordings against.</summary>
        public static async Task<HashSet<string>> CuratedNamesAsync(ApplicationDbContext db) =>
            (await db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == (long)CurationStatusEnum.Curated && i.IngredientNutrients.Any(n => !n.IsDeleted))
                .Select(i => i.Name)
                .ToListAsync())
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
