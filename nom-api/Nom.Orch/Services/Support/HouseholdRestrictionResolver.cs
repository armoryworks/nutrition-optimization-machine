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
        /// <summary>Any active restriction judged by what a dish contains rather than by the dish's own data: a direct restriction on a food that is not a prepared food, or a category criterion that names a food that is not a prepared food, an ILIKE or word pattern, or a capped alcohol amount (judged on a recipe's drink lines). Such a restriction can show a prepared food fits only through its contents read from an authoritative statement (<see cref="PreparedContentsGate"/>, <see cref="Nom.Data.Nutrition.PreparedFoods"/>).</summary>
        public bool ReadsContents { get; set; }
        /// <summary>Recipes forbidden by a line's own text rather than its food, or as a whole: a drink line linked to a food that is not a drink (<see cref="DietCriteriaMatcher.DrinkLineRecipesAsync"/>), a line whose own text names a pattern of the restriction on any food (<see cref="DietCriteriaMatcher.TypeProbe.LineRecipes"/>), a line that names the restricted thing on a food a pattern matched only through an alias that does not count (<see cref="DietCriteriaMatcher.TypeProbe.AliasLineRecipes"/>), or more retained drink alcohol per serving than a capped alcohol criterion allows (<see cref="DietCriteriaMatcher.RetainedRecipesAsync"/>).</summary>
        public HashSet<long> RecipeIds { get; } = new();
        /// <summary>The nutrient, word and name-pattern criteria of the restrictions, which <see cref="PreparedContentsGate"/> reads a prepared meal's named ingredients and allergens against as a recipe's lines are read.</summary>
        public List<DietDataCriterion> Criteria { get; } = new();
        /// <summary>The foods the restrictions name directly (a restriction's own food or a type's named-food criterion), each with the highest severity that names it, which <see cref="PreparedContentsGate"/> reads a prepared meal's allergens and recognised terms against.</summary>
        public Dictionary<long, int> NamedFoods { get; } = new();
        /// <summary>Whether a food probe the restrictions need is not kept yet (before the diet gate's warm has computed it): the foods are not all known, so <see cref="RecipeIds"/> holds every recipe and <see cref="PreparedContentsGate"/> holds every prepared meal (staging checkpoint 4, P1).</summary>
        public bool Pending { get; set; }
        /// <summary>The stored restriction sets the restrictions read (staging checkpoint 5, Q2), when requests read the diet gate's stored results: <see cref="IngredientIds"/> and <see cref="RecipeIds"/> then hold only what the restrictions name directly, and <see cref="DietGateStore.WithoutHeld(System.Linq.IQueryable{Nom.Data.Recipe.RecipeEntity}, ApplicationDbContext, RestrictedIngredientSet)"/> reads the rest from the stored rows.</summary>
        public StoredDietGate? Stored { get; set; }
        public static readonly RestrictedIngredientSet Empty = new();

        /// <summary>Adds to <see cref="IngredientIds"/> those of the foods a stored set counts, so a reader of a few foods (a prepared meal and what its statement names) sees them as the matcher would; nothing outside the store.</summary>
        public async Task LoadFoodsAsync(ApplicationDbContext db, IEnumerable<long> foodIds)
        {
            if (Stored == null) return;
            var ids = foodIds.Where(id => !IngredientIds.Contains(id)).Distinct().ToList();
            IngredientIds.UnionWith(await DietGateStore.CountedAsync(db, this, ids));
        }

        internal void Name(long foodId, int severity) =>
            NamedFoods[foodId] = Math.Max(severity, NamedFoods.GetValueOrDefault(foodId));
    }

    /// <summary>
    /// Turns a household's restrictions into a concrete set of ingredient ids that meal
    /// planning, search and top-up must avoid. Restrictions saved through the UI reference
    /// a *category* (RestrictionTypeId, e.g. "Nut Allergy"); the category's curated
    /// <see cref="RestrictionCriterionEntity"/> rows (exact ingredient, name pattern read by words, word
    /// pattern or nutrient, with severity; see <see cref="DietCriteriaMatcher"/>, which reads a food by
    /// its own name, category and trusted aliases and a recipe line by its own text) are what make it enforceable. Direct IngredientId restrictions still
    /// count too. Plan-wide restrictions (PersonId NULL) apply through the members' plans.
    /// </summary>
    public sealed class HouseholdRestrictionResolver
    {
        private readonly ApplicationDbContext _db;
        private readonly DietGateCache? _cache;
        private bool? _store;

        /// <summary>
        /// A resolver on the context. Without <paramref name="cache"/>, the sets are read from the diet gate's stored
        /// results (<see cref="DietGateStore"/>, staging checkpoint 5, Q2): always on a relational database, and on another
        /// once a build is active (or as <paramref name="store"/> says). With a cache, or with <paramref name="store"/>
        /// false, they are read in line by the matcher (<see cref="DietCriteriaMatcher"/>), its whole-catalog probes kept in
        /// the cache: the reading the store is compared with in the tests.
        /// </summary>
        public HouseholdRestrictionResolver(ApplicationDbContext db, DietGateCache? cache = null, bool? store = null)
        {
            _db = db;
            _cache = cache;
            _store = cache != null ? false : store ?? (db.Database.IsRelational() ? true : null);
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
            var severeTypes = new HashSet<long>();
            foreach (var r in restrictions)
            {
                if (r.IngredientId is { } id)
                {
                    result.IngredientIds.Add(id);
                    namedFoodIds.Add(id);
                    result.Name(id, r.Severity ?? 0);
                }
                if ((r.Severity ?? 0) >= 4) result.HasSevere = true;
                if ((r.Severity ?? 0) >= 4 && r.RestrictionTypeId is { } severeType) severeTypes.Add(severeType);
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
                        result.Name(id, c.Severity);
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
                    .Select(i => new { i.Id, i.FdcCategoryId, i.FdcDataType, i.FdcCategory })
                    .ToListAsync();
                result.ReadsContents = namedFoodIds.Any(id =>
                    !preparedFoods.Any(f => f.Id == id && Nom.Data.Nutrition.PreparedFoods.IsPrepared(f.FdcCategoryId, f.FdcDataType, f.FdcCategory)));
            }

            if (typeIds.Count > 0) await AddDataCriteriaAsync(result, typeIds, 0, severeTypes);
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
                result.Name(r.IngredientId!.Value, r.Severity ?? 0);
                result.HasSevere = true;
            }

            var typeIds = restrictions.Where(r => r.RestrictionTypeId != null).Select(r => r.RestrictionTypeId!.Value).Distinct().ToList();
            if (typeIds.Count == 0) return result;

            var criteria = await _db.Set<RestrictionCriterionEntity>()
                .Where(c => typeIds.Contains(c.RestrictionTypeId) && c.Severity >= 4)
                .Select(c => new { c.IngredientId, c.IngredientPattern, c.Severity })
                .ToListAsync();
            if (criteria.Count == 0) return result;

            result.HasSevere = true;
            foreach (var c in criteria.Where(c => c.IngredientId != null))
            {
                result.IngredientIds.Add(c.IngredientId!.Value);
                result.Name(c.IngredientId!.Value, c.Severity);
            }

            await AddDataCriteriaAsync(result, typeIds, 4);
            return result;
        }

        /// <summary>What the criteria of the restriction types forbid at or above a severity, as a household set with those restrictions reads them (one type read alone).</summary>
        public async Task<RestrictedIngredientSet> ResolveTypesAsync(IReadOnlyCollection<long> typeIds, int minimumSeverity = 0)
        {
            var result = new RestrictedIngredientSet();
            if (typeIds.Count == 0) return result;
            var named = await _db.Set<RestrictionCriterionEntity>()
                .Where(c => typeIds.Contains(c.RestrictionTypeId) && c.Severity >= minimumSeverity)
                .Select(c => new { c.IngredientId, c.Severity })
                .ToListAsync();
            foreach (var c in named)
            {
                if (c.Severity >= 4) result.HasSevere = true;
                if (c.IngredientId is not { } id) continue;
                result.IngredientIds.Add(id);
                result.Name(id, c.Severity);
            }
            await AddDataCriteriaAsync(result, typeIds, minimumSeverity);
            return result;
        }

        /// <summary>
        /// Adds the foods the nutrient, word and name-pattern criteria of the types match, the
        /// recipes with a line whose own text names a pattern of the type on any food, the
        /// recipes whose lines on a food matched only through an alias that does not count name the
        /// restricted thing themselves, the recipes whose drink lines are linked to none of the
        /// foods, and the recipes whose retained drink alcohol is above a capped alcohol criterion
        /// (<see cref="DietCriteriaMatcher"/>).
        /// </summary>
        private async Task AddDataCriteriaAsync(RestrictedIngredientSet result, IReadOnlyCollection<long> typeIds, int minimumSeverity, IReadOnlySet<long>? severeTypes = null)
        {
            var data = await DietCriteriaMatcher.CriteriaAsync(_db, typeIds, minimumSeverity);
            if (data.Count == 0) return;
            result.Criteria.AddRange(data);
            _store ??= await DietGateStore.InUseAsync(_db);
            if (_store == true)
            {
                var stored = await DietGateStore.SetsAsync(_db, data.GroupBy(c => c.RestrictionTypeId).Select(g => (IReadOnlyList<DietDataCriterion>)g.ToList()), severeTypes ?? new HashSet<long>());
                if (stored == null) result.Pending = true;
                else result.Stored = stored;
                return;
            }
            foreach (var probe in (await DietCriteriaMatcher.ProbeAsync(_db, data, cache: _cache)).Values)
            {
                foreach (var ids in probe.Foods.Values) result.IngredientIds.UnionWith(ids);
                result.RecipeIds.UnionWith(probe.AliasLineRecipes);
                result.RecipeIds.UnionWith(probe.LineRecipes);
            }
            result.RecipeIds.UnionWith(await DietCriteriaMatcher.DrinkLineRecipesAsync(_db, data, result.IngredientIds, _cache));
            result.RecipeIds.UnionWith(await DietCriteriaMatcher.RetainedRecipesAsync(_db, data, _cache));
        }
    }
}
