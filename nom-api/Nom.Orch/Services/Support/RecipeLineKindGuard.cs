using System;
using System.Collections.Generic;
using System.Linq;
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
    /// wider rule, a short line that sits on a curated food, is applied only when asked for.
    /// </summary>
    public static class RecipeLineKindGuard
    {
        public const int ShortLineLength = 40;
        public const string QuantityReason = "states an amount";
        public const string CuratedNameReason = "names a curated food";
        public const string ShortLineReason = "short line on a curated food";

        /// <summary>
        /// Why <paramref name="kind"/> may not stand on this line, or null when it may. Only a kind
        /// that is not an ingredient is ever refused.
        /// </summary>
        public static string? Refusal(string? kind, decimal quantity, string? rawLine, bool onCuratedFood, IReadOnlySet<string> curatedNames, bool shortLinesOnCuratedFoods = false)
        {
            if (!RecipeIngredientLineKind.IsExempt(kind)) return null;
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
