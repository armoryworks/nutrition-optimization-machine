using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Which recipes a journaled line operation still holds. Every such operation journals its
    /// lines under EntityType RecipeIngredient with the recipe as EntityId, except an applied USDA
    /// link, which names the recipe in each moved line's value; an undo removes or relabels its
    /// entry, which frees the recipe again. Background writers (the start-up repair, the repair and
    /// line-classify lanes) leave the lines of a held recipe as they are, so the undo restores every
    /// row it wrote however often the API restarts in between. Only the line labels of the classify
    /// lane, which no undo reads, and the start-up repair's own entries hold no recipe.
    /// </summary>
    public static class RecipeLineHold
    {
        private static readonly string[] Unheld =
        {
            ScrapedRecipeRepairService.FillChangeType, RecipeLineClassificationService.ClassifyChangeType, RecipeLineClassificationService.SuggestedChangeType,
        };

        /// <summary>The held recipes among <paramref name="recipeIds"/>; entries of the change types in <paramref name="alsoUnheld"/> (a writer's own) hold none.</summary>
        public static async Task<HashSet<long>> HeldAsync(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken = default, params string[] alsoUnheld)
        {
            var ids = recipeIds.ToList();
            var unheld = Unheld.Concat(alsoUnheld).ToList();
            var held = (await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ScrapedRecipeRepairService.LineEntityType && ids.Contains(a.EntityId) && !unheld.Contains(a.ChangeType))
                    .Select(a => a.EntityId)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var keys = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            var linked = db.Database.IsRelational()
                ? await db.Database.SqlQuery<string>($"""
                    SELECT DISTINCT split_part(a."NewValue", ':', 1) AS "Value"
                    FROM audit."AuditLogEntry" a
                    WHERE a."EntityType" = {LinkApplyJournal.EntityType} AND a."ChangeType" = {LinkApplyJournal.ChangeType}
                      AND a."PropertyName" = {LinkApplyJournal.Line} AND split_part(a."NewValue", ':', 1) = ANY({keys})
                    """).ToListAsync(cancellationToken)
                : (await db.AuditLogEntries.AsNoTracking()
                        .Where(a => a.EntityType == LinkApplyJournal.EntityType && a.ChangeType == LinkApplyJournal.ChangeType && a.PropertyName == LinkApplyJournal.Line)
                        .Select(a => a.NewValue)
                        .ToListAsync(cancellationToken))
                    .Select(v => (v ?? string.Empty).Split(':')[0])
                    .Where(keys.Contains)
                    .ToList();
            foreach (var key in linked)
            {
                if (long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) held.Add(id);
            }
            return held;
        }
    }
}
