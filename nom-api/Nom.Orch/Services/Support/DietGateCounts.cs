using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Orch.Services.Support
{
    /// <summary>What the diet gate keeps out for one restriction type, with all its criteria (<c>all</c>) or those of severity 4 and 5 a person's search reads (<c>severe</c>).</summary>
    /// <param name="Foods">The foods its criteria count.</param>
    /// <param name="RecipesByFood">The recipes that are not deleted with a live line on one of those foods.</param>
    /// <param name="RecipesByText">The recipes that are not deleted kept out as a whole: a line's own text, a drink line, retained alcohol, or not yet read.</param>
    /// <param name="RecipesHeld">Both together.</param>
    /// <param name="ReadUpTo">The recipe id up to which the line index has read what the type needs; null when it reads no line.</param>
    /// <param name="Pending">Whether a food probe of the type is not kept yet, so every recipe is held.</param>
    public sealed record DietGateTypeCount(long TypeId, string Name, string Reading, int Severity, int Foods, int RecipesByFood, int RecipesByText, int RecipesHeld, long? ReadUpTo, bool Pending);

    /// <summary>The diet gate's count over every restriction type with a criterion, the recipes that are not deleted (<paramref name="Recipes"/>, the base of a share), and the line index it was read from.</summary>
    public sealed record DietGateCountsReport(int Recipes, DateTime? IndexAt, int IndexRecipes, int IndexLines, int Rules, long IndexReadUpTo, IReadOnlyList<DietGateTypeCount> Types);

    /// <summary>
    /// The diet gate's count per restriction type, read from what the warm keeps (the line index and the food probes)
    /// and never probing the catalog, so it answers in seconds where <c>ops/diet-gate-count.sql</c> takes about 40
    /// minutes on the production copy (staging checkpoint 4, P23). Each type is read as a household set with that one
    /// restriction reads it (<see cref="HouseholdRestrictionResolver.ResolveTypesAsync"/>): the same foods and recipes
    /// plans, search and the prepared check keep out. Only recipes that are not deleted are counted, as
    /// <c>ops/diet-gate-count.sql</c> counts them (review CK9S-4).
    /// </summary>
    public static class DietGateCounts
    {
        /// <summary>The count of every restriction type with a criterion; types not warm yet show as pending.</summary>
        public static async Task<DietGateCountsReport> ReadAsync(ApplicationDbContext db, DietGateCache cache, DietLineIndex index)
        {
            var typeIds = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync();
            var names = await db.Set<ReferenceEntity>().AsNoTracking()
                .Where(r => typeIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name })
                .ToDictionaryAsync(r => r.Id, r => r.Name);
            var severities = (await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                    .Where(c => typeIds.Contains(c.RestrictionTypeId))
                    .Select(c => new { c.RestrictionTypeId, c.Severity })
                    .ToListAsync())
                .GroupBy(c => c.RestrictionTypeId)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Severity));
            var live = (await db.Recipes.IgnoreQueryFilters().AsNoTracking().Where(r => !r.IsDeleted).Select(r => r.Id).ToListAsync()).ToHashSet();
            var snapshot = index.Current(db);
            var resolver = new HouseholdRestrictionResolver(db, cache, index);
            var types = new List<DietGateTypeCount>();
            foreach (var typeId in typeIds.OrderBy(id => names.GetValueOrDefault(id), StringComparer.OrdinalIgnoreCase).ThenBy(id => id))
            {
                foreach (var (reading, minimum) in new[] { ("all", 0), ("severe", 4) })
                {
                    if (minimum > 0 && severities.GetValueOrDefault(typeId) < minimum) continue;
                    var set = await resolver.ResolveTypesAsync(new[] { typeId }, minimum);
                    var byFood = snapshot?.RecipesOn(set.IngredientIds) ?? new HashSet<long>();
                    byFood.IntersectWith(live);
                    var byText = new HashSet<long>(set.RecipeIds);
                    byText.IntersectWith(live);
                    var held = new HashSet<long>(byFood);
                    held.UnionWith(byText);
                    types.Add(new DietGateTypeCount(typeId, names.GetValueOrDefault(typeId) ?? "#" + typeId, reading, severities.GetValueOrDefault(typeId),
                        set.IngredientIds.Count, byFood.Count, byText.Count, held.Count, set.ReadUpTo, set.Pending));
                }
            }
            var coverage = snapshot?.Coverage;
            return new DietGateCountsReport(live.Count, snapshot?.At, snapshot?.RecipeCount ?? 0, snapshot?.LineCount ?? 0, coverage?.Rules ?? 0, coverage?.ReadUpTo ?? 0, types);
        }
    }
}
