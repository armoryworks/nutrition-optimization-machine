using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>What a household's active dietary restrictions forbid, in ingredient terms.</summary>
    public sealed class RestrictedIngredientSet
    {
        public HashSet<long> IngredientIds { get; } = new();
        /// <summary>Any active restriction (or its category's criteria) at severity ≥ 4 — allergy/medical.</summary>
        public bool HasSevere { get; set; }
        /// <summary>Any active restriction judged by what a dish contains rather than by the dish's own data: a direct restriction on a food that is not a prepared food, or a category criterion that names a food that is not a prepared food, an ILIKE or word pattern, or a capped alcohol amount (judged on a recipe's drink lines). A prepared food lists no ingredients, so such a restriction cannot show one fits (<see cref="Nom.Data.Nutrition.PreparedFoods"/>).</summary>
        public bool ReadsContents { get; set; }
        /// <summary>Recipes forbidden by a line's own text rather than its food, or as a whole: a drink line linked to a food that is not a drink (<see cref="DietCriteriaMatcher.DrinkLineRecipesAsync"/>), a line that names the restricted thing on a food a pattern matched only through an alias that does not count (<see cref="DietCriteriaMatcher.TypeProbe.AliasLineRecipes"/>), or more retained drink alcohol per serving than a capped alcohol criterion allows (<see cref="DietCriteriaMatcher.RetainedRecipesAsync"/>).</summary>
        public HashSet<long> RecipeIds { get; } = new();
        public static readonly RestrictedIngredientSet Empty = new();
    }

    /// <summary>
    /// Turns a household's restrictions into a concrete set of ingredient ids that meal
    /// planning, search and top-up must avoid. Restrictions saved through the UI reference
    /// a *category* (RestrictionTypeId, e.g. "Nut Allergy"); the category's curated
    /// <see cref="RestrictionCriterionEntity"/> rows (exact ingredient, ILIKE name pattern, word
    /// pattern or nutrient, with severity; see <see cref="DietCriteriaMatcher"/>, which reads a food by
    /// its own name, category and trusted aliases and a recipe line by its own text) are what make it enforceable. Direct IngredientId restrictions still
    /// count too. Plan-wide restrictions (PersonId NULL) apply through the members' plans.
    /// </summary>
    public sealed class HouseholdRestrictionResolver
    {
        private readonly ApplicationDbContext _db;
        private readonly DietGateCache? _cache;

        /// <summary>A resolver on the context; the diet gate's whole-catalog probes go through <paramref name="cache"/>, or through <see cref="DietGateCache.Shared"/> on a relational database when none is given.</summary>
        public HouseholdRestrictionResolver(ApplicationDbContext db, DietGateCache? cache = null)
        {
            _db = db;
            _cache = cache ?? (db.Database.IsRelational() ? DietGateCache.Shared : null);
        }

        public async Task<RestrictedIngredientSet> ResolveAsync(long householdId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var memberIds = await _db.HouseholdMembers
                .Where(hm => hm.HouseholdId == householdId && hm.IsActive)
                .Select(hm => hm.PersonId)
                .Distinct()
                .ToListAsync();
            if (memberIds.Count == 0) return RestrictedIngredientSet.Empty;

            var memberPlanIds = await _db.PlanParticipants
                .Where(pp => memberIds.Contains(pp.PersonId))
                .Select(pp => pp.PlanId)
                .Distinct()
                .ToListAsync();

            var restrictions = await _db.Restrictions
                .Where(r => (r.EndDate == null || r.EndDate >= today)
                         && (r.BeginDate == null || r.BeginDate <= today)
                         && ((r.PersonId != null && memberIds.Contains(r.PersonId.Value))
                             || (r.PersonId == null && r.PlanId != null && memberPlanIds.Contains(r.PlanId.Value))))
                .Select(r => new { r.IngredientId, r.RestrictionTypeId, r.Severity })
                .ToListAsync();
            if (restrictions.Count == 0) return RestrictedIngredientSet.Empty;

            var result = new RestrictedIngredientSet();
            var namedFoodIds = new HashSet<long>();
            foreach (var r in restrictions)
            {
                if (r.IngredientId is { } id)
                {
                    result.IngredientIds.Add(id);
                    namedFoodIds.Add(id);
                }
                if ((r.Severity ?? 0) >= 4) result.HasSevere = true;
            }

            var typeIds = restrictions.Where(r => r.RestrictionTypeId != null).Select(r => r.RestrictionTypeId!.Value).Distinct().ToList();
            if (typeIds.Count > 0)
            {
                var criteria = await _db.Set<RestrictionCriterionEntity>()
                    .Where(c => typeIds.Contains(c.RestrictionTypeId))
                    .Select(c => new { c.IngredientId, c.IngredientPattern, c.Severity, c.MaxAmountPerServing, NutrientName = c.Nutrient != null ? c.Nutrient.Name : null })
                    .ToListAsync();

                foreach (var c in criteria)
                {
                    if (c.IngredientId is { } id)
                    {
                        result.IngredientIds.Add(id);
                        namedFoodIds.Add(id);
                    }
                    if (c.Severity >= 4) result.HasSevere = true;
                    if (!string.IsNullOrWhiteSpace(c.IngredientPattern)) result.ReadsContents = true;
                    if (c.MaxAmountPerServing != null && DietCriteriaMatcher.IsAlcohol(c.NutrientName)) result.ReadsContents = true;
                }
            }

            if (!result.ReadsContents && namedFoodIds.Count > 0)
            {
                var preparedFoods = await _db.Ingredients.IgnoreQueryFilters()
                    .Where(i => namedFoodIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.FdcCategoryId })
                    .ToListAsync();
                result.ReadsContents = namedFoodIds.Any(id =>
                    !preparedFoods.Any(f => f.Id == id && Nom.Data.Nutrition.PreparedFoods.IsPrepared(f.FdcCategoryId)));
            }

            if (typeIds.Count > 0) await AddDataCriteriaAsync(result, typeIds, 0);
            return result;
        }

        /// <summary>
        /// What one person's own active restrictions forbid at severity ≥ 4 (allergy/medical): the
        /// rule that marks a recipe as a severe diet conflict for them (a direct ingredient restriction
        /// counts by its own severity, a category by its criteria's). <see cref="RestrictedIngredientSet.HasSevere"/>
        /// is true when any such restriction applies, even before its criteria match an ingredient.
        /// </summary>
        public async Task<RestrictedIngredientSet> ResolveSevereForPersonAsync(long personId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var restrictions = await _db.Restrictions
                .Where(r => r.PersonId == personId
                         && (r.BeginDate == null || r.BeginDate <= today)
                         && (r.EndDate == null || r.EndDate >= today))
                .Select(r => new { r.IngredientId, r.RestrictionTypeId, r.Severity })
                .ToListAsync();
            if (restrictions.Count == 0) return RestrictedIngredientSet.Empty;

            var result = new RestrictedIngredientSet();
            foreach (var r in restrictions.Where(r => r.IngredientId != null && (r.Severity ?? 0) >= 4))
            {
                result.IngredientIds.Add(r.IngredientId!.Value);
                result.HasSevere = true;
            }

            var typeIds = restrictions.Where(r => r.RestrictionTypeId != null).Select(r => r.RestrictionTypeId!.Value).Distinct().ToList();
            if (typeIds.Count == 0) return result;

            var criteria = await _db.Set<RestrictionCriterionEntity>()
                .Where(c => typeIds.Contains(c.RestrictionTypeId) && c.Severity >= 4)
                .Select(c => new { c.IngredientId, c.IngredientPattern })
                .ToListAsync();
            if (criteria.Count == 0) return result;

            result.HasSevere = true;
            foreach (var c in criteria.Where(c => c.IngredientId != null))
                result.IngredientIds.Add(c.IngredientId!.Value);

            await AddDataCriteriaAsync(result, typeIds, 4);
            return result;
        }

        /// <summary>
        /// Adds the foods the nutrient, word and name-pattern criteria of the types match, the
        /// recipes whose lines on a food matched only through an alias that does not count name the
        /// restricted thing themselves, the recipes whose drink lines are linked to none of the
        /// foods, and the recipes whose retained drink alcohol is above a capped alcohol criterion
        /// (<see cref="DietCriteriaMatcher"/>).
        /// </summary>
        private async Task AddDataCriteriaAsync(RestrictedIngredientSet result, IReadOnlyCollection<long> typeIds, int minimumSeverity)
        {
            var data = await DietCriteriaMatcher.CriteriaAsync(_db, typeIds, minimumSeverity);
            if (data.Count == 0) return;
            foreach (var probe in (await DietCriteriaMatcher.ProbeAsync(_db, data, cache: _cache)).Values)
            {
                foreach (var ids in probe.Foods.Values) result.IngredientIds.UnionWith(ids);
                result.RecipeIds.UnionWith(probe.AliasLineRecipes);
            }
            result.RecipeIds.UnionWith(await DietCriteriaMatcher.DrinkLineRecipesAsync(_db, data, result.IngredientIds, _cache));
            result.RecipeIds.UnionWith(await DietCriteriaMatcher.RetainedRecipesAsync(_db, data, _cache));
        }
    }
}
