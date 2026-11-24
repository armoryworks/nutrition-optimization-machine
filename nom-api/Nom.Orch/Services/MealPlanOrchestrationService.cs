// File: Nom.Orch/Services/MealPlan/MealPlanOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Household;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class MealPlanOrchestrationService : IMealPlanOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPolicyEnforcementService _policy;

        public MealPlanOrchestrationService(ApplicationDbContext context, IPolicyEnforcementService policy)
        {
            _context = context;
            _policy = policy;
        }

        public async Task<List<MealPlanResponseModel>> GetAllMealPlansAsync(DateTime? startDate = null, DateTime? endDate = null, List<long>? householdIds = null)
        {
            var query = _context.MealPlans
                .Include(mp => mp.Recipe)
                .Include(mp => mp.MealType)
                .AsQueryable();

            if (householdIds != null)
                query = query.Where(mp => householdIds.Contains(mp.HouseholdId));
            if (startDate.HasValue)
                query = query.Where(mp => mp.Date >= DateOnly.FromDateTime(startDate.Value));
            if (endDate.HasValue)
                query = query.Where(mp => mp.Date <= DateOnly.FromDateTime(endDate.Value));

            var mealPlans = await query.ToListAsync();
            var removed = await RemovedAsync(mealPlans);

            return mealPlans.Select(mp => Removed(new MealPlanResponseModel
            {
                Id = mp.Id,
                HouseholdId = mp.HouseholdId,
                AuthorId = mp.AuthorId,
                Date = mp.Date,
                MealTypeId = mp.MealTypeId,
                MealType = mp.MealType?.Name ?? "Meal",
                Title = ProseWithholding.EntryTitle(mp.Title, mp.Recipe),
                Notes = mp.Note,
                RecipeId = mp.RecipeId,
                RecipeName = mp.Recipe?.Name,
                CreatedDate = mp.CreatedDate,
                ModifiedDate = mp.LastModifiedDate
            }, removed)).ToList();
        }

        private async Task<Dictionary<long, RemovedRecipeModel>> RemovedAsync(IEnumerable<MealPlanEntity> entries) =>
            await RecipeRemovalService.RemovedAsync(_context, entries.Where(e => e.RecipeId != null && e.Recipe == null).Select(e => e.RecipeId!.Value));

        /// <summary>An entry of a removed recipe keeps its title, the source link and the plain note; nothing of the recipe's wording.</summary>
        private static MealPlanResponseModel Removed(MealPlanResponseModel model, IReadOnlyDictionary<long, RemovedRecipeModel> removed)
        {
            if (model.RecipeId is not { } id || !removed.TryGetValue(id, out var gone)) return model;
            model.Title = gone.Title;
            model.RecipeName = gone.Title;
            model.RecipeRemoved = true;
            model.RecipeSourceUrl = gone.SourceUrl;
            model.RecipeRemovedNote = RecipeRemovalService.RemovedNote;
            return model;
        }

        public async Task<MealPlanCreateResponseModel> CreateMealPlanAsync(MealPlanCreateModel model, long authorId)
        {
            await ValidateSlotAssignmentAsync(model.HouseholdId, authorId, model.RecipeId, model.IngredientId);
            if (model.IngredientId.HasValue && !model.ConfirmHeld
                && (await CheckPreparedMealsAsync(model.HouseholdId, new[] { model.IngredientId.Value })).Any(c => c.Severe))
                throw new InvalidOperationException("restriction_violation:prepared_held_unconfirmed");

            var mealPlan = new MealPlanEntity
            {
                HouseholdId = model.HouseholdId,
                AuthorId = authorId,
                Date = model.Date,
                MealTypeId = model.MealTypeId,
                Title = model.Title,
                Note = model.Notes,
                RecipeId = model.RecipeId,
                IngredientId = model.IngredientId,
                Quantity = model.IngredientId.HasValue ? (model.Quantity ?? 1m) : null,
                MeasurementId = model.IngredientId.HasValue ? model.MeasurementId : null,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.MealPlans.Add(mealPlan);
            await _context.SaveChangesAsync();

            return new MealPlanCreateResponseModel
            {
                Id = mealPlan.Id,
                HouseholdId = mealPlan.HouseholdId,
                AuthorId = mealPlan.AuthorId,
                Date = mealPlan.Date,
                MealTypeId = mealPlan.MealTypeId,
                Title = ProseWithholding.EntryTitle(mealPlan.Title, mealPlan.Recipe),
                Notes = mealPlan.Note,
                RecipeId = mealPlan.RecipeId,
                CreatedDate = mealPlan.CreatedDate
            };
        }

        public async Task<MealPlanResponseModel?> GetMealPlanAsync(long id)
        {
            var mealPlan = await _context.MealPlans
                .Include(mp => mp.Recipe)
                .Include(mp => mp.MealType)
                .FirstOrDefaultAsync(mp => mp.Id == id);

            if (mealPlan == null)
                return null;

            return Removed(new MealPlanResponseModel
            {
                Id = mealPlan.Id,
                HouseholdId = mealPlan.HouseholdId,
                AuthorId = mealPlan.AuthorId,
                Date = mealPlan.Date,
                MealTypeId = mealPlan.MealTypeId,
                MealType = mealPlan.MealType?.Name ?? "Meal",
                Title = ProseWithholding.EntryTitle(mealPlan.Title, mealPlan.Recipe),
                Notes = mealPlan.Note,
                RecipeId = mealPlan.RecipeId,
                RecipeName = mealPlan.Recipe?.Name,
                CreatedDate = mealPlan.CreatedDate,
                ModifiedDate = mealPlan.LastModifiedDate
            }, await RemovedAsync(new[] { mealPlan }));
        }

        public async Task<MealPlanResponseModel?> UpdateMealPlanAsync(long id, MealPlanUpdateModel model)
        {
            var mealPlan = await _context.MealPlans
                .Include(mp => mp.Recipe)
                .Include(mp => mp.MealType)
                .FirstOrDefaultAsync(mp => mp.Id == id);
            if (mealPlan == null)
                return null;

            await ValidateSlotAssignmentAsync(mealPlan.HouseholdId, mealPlan.AuthorId, model.RecipeId, model.IngredientId);

            mealPlan.Date = model.Date;
            mealPlan.MealTypeId = model.MealTypeId;
            mealPlan.Title = model.Title;
            mealPlan.Note = model.Notes;
            mealPlan.RecipeId = model.RecipeId;
            mealPlan.IngredientId = model.IngredientId;
            mealPlan.Quantity = model.IngredientId.HasValue ? (model.Quantity ?? 1m) : null;
            mealPlan.MeasurementId = model.IngredientId.HasValue ? model.MeasurementId : null;
            mealPlan.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Reload MealType reference if it changed
            if (mealPlan.MealType == null || mealPlan.MealType.Id != model.MealTypeId)
            {
                await _context.Entry(mealPlan).Reference(mp => mp.MealType).LoadAsync();
            }

            return new MealPlanResponseModel
            {
                Id = mealPlan.Id,
                HouseholdId = mealPlan.HouseholdId,
                AuthorId = mealPlan.AuthorId,
                Date = mealPlan.Date,
                MealTypeId = mealPlan.MealTypeId,
                MealType = mealPlan.MealType?.Name ?? "Meal",
                Title = ProseWithholding.EntryTitle(mealPlan.Title, mealPlan.Recipe),
                Notes = mealPlan.Note,
                RecipeId = mealPlan.RecipeId,
                RecipeName = mealPlan.Recipe?.Name,
                CreatedDate = mealPlan.CreatedDate,
                ModifiedDate = mealPlan.LastModifiedDate
            };
        }

        public async Task<bool> DeleteMealPlanAsync(long id)
        {
            var mealPlan = await _context.MealPlans.FindAsync(id);
            if (mealPlan == null)
                return false;

            _context.MealPlans.Remove(mealPlan);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MarkShoppingCompletedAsync(long mealPlanId)
        {
            var mealPlan = await _context.MealPlans.FindAsync(mealPlanId);
            if (mealPlan == null)
                return false;

            mealPlan.ShoppingCompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<FoodGroupModel>> GetFoodGroupsAsync()
        {
            return await _context.FoodGroupTypes
                .OrderBy(fg => fg.ReferenceId)
                .Select(fg => new FoodGroupModel
                {
                    Id = fg.ReferenceId,
                    Name = fg.ReferenceName,
                    Description = fg.ReferenceDescription
                })
                .ToListAsync();
        }

        public async Task<List<FoodGroupRuleModel>> GetFoodGroupRulesAsync(long householdId)
        {
            return await _context.FoodGroupRules
                .Include(r => r.FoodGroup)
                .Include(r => r.MealType)
                .Where(r => r.HouseholdId == householdId)
                .OrderBy(r => r.FoodGroupId)
                .Select(r => new FoodGroupRuleModel
                {
                    Id = r.Id,
                    HouseholdId = r.HouseholdId,
                    FoodGroupId = r.FoodGroupId,
                    FoodGroupName = r.FoodGroup != null ? r.FoodGroup.Name : null,
                    MinServings = r.MinServings,
                    Timeframe = r.Timeframe.ToString(),
                    MealTypeId = r.MealTypeId,
                    MealTypeName = r.MealType != null ? r.MealType.Name : null,
                    IsActive = r.IsActive
                })
                .ToListAsync();
        }

        public async Task<FoodGroupRuleModel> UpsertFoodGroupRuleAsync(FoodGroupRuleUpsertModel model)
        {
            if (model.MinServings <= 0)
                throw new ArgumentException("MinServings must be greater than zero.");

            var timeframe = Enum.TryParse<FoodGroupRuleTimeframe>(model.Timeframe, true, out var tf)
                ? tf : FoodGroupRuleTimeframe.PerDay;

            // One rule per (household, food group, timeframe, meal-type scope) — update in place if it exists.
            var rule = await _context.FoodGroupRules.FirstOrDefaultAsync(r =>
                r.HouseholdId == model.HouseholdId
                && r.FoodGroupId == model.FoodGroupId
                && r.Timeframe == timeframe
                && r.MealTypeId == model.MealTypeId);

            if (rule == null)
            {
                rule = new FoodGroupRuleEntity
                {
                    HouseholdId = model.HouseholdId,
                    FoodGroupId = model.FoodGroupId,
                    Timeframe = timeframe,
                    MealTypeId = model.MealTypeId,
                    CreatedDate = DateTime.UtcNow,
                };
                _context.FoodGroupRules.Add(rule);
            }

            rule.MinServings = model.MinServings;
            rule.IsActive = model.IsActive;
            rule.LastModifiedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _context.Entry(rule).Reference(r => r.FoodGroup).LoadAsync();
            if (rule.MealTypeId.HasValue)
                await _context.Entry(rule).Reference(r => r.MealType).LoadAsync();

            return new FoodGroupRuleModel
            {
                Id = rule.Id,
                HouseholdId = rule.HouseholdId,
                FoodGroupId = rule.FoodGroupId,
                FoodGroupName = rule.FoodGroup?.Name,
                MinServings = rule.MinServings,
                Timeframe = rule.Timeframe.ToString(),
                MealTypeId = rule.MealTypeId,
                MealTypeName = rule.MealType?.Name,
                IsActive = rule.IsActive
            };
        }

        public async Task<bool> DeleteFoodGroupRuleAsync(long id, long householdId)
        {
            // Bind the delete to the household the caller was authorized for — the id
            // alone would let a manager of household A delete household B's rule.
            var rule = await _context.FoodGroupRules.FirstOrDefaultAsync(r => r.Id == id && r.HouseholdId == householdId);
            if (rule == null) return false;
            _context.FoodGroupRules.Remove(rule);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<MealPlanRuleResponseModel> CreateRuleAsync(MealPlanRuleCreateModel model)
        {
            var rule = new MealPlanRuleEntity
            {
                HouseholdId = model.HouseholdId,
                DayOfWeekId = model.DayOfWeekId,
                MealTypeId = model.MealTypeId,
                QueryFilter = model.QueryFilter,
                MaxRecipes = model.MaxRecipes,
                IsActive = model.IsActive,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.MealPlanRules.Add(rule);
            await _context.SaveChangesAsync();

            return (await GetRuleAsync(rule.Id))!;
        }

        public async Task<MealPlanRuleResponseModel?> GetRuleAsync(long id)
        {
            var rule = await _context.MealPlanRules
                .Include(r => r.DayOfWeek)
                .Include(r => r.MealType)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return null;

            return new MealPlanRuleResponseModel
            {
                Id = rule.Id,
                HouseholdId = rule.HouseholdId,
                DayOfWeekId = rule.DayOfWeekId,
                DayOfWeekName = rule.DayOfWeek?.Name,
                MealTypeId = rule.MealTypeId,
                MealTypeName = rule.MealType?.Name,
                QueryFilter = rule.QueryFilter ?? string.Empty,
                MaxRecipes = rule.MaxRecipes ?? 3,
                IsActive = rule.IsActive,
                CreatedDate = rule.CreatedDate,
                ModifiedDate = rule.LastModifiedDate
            };
        }

        public async Task<List<MealPlanRuleResponseModel>> GetRulesAsync(long householdId)
        {
            return await _context.MealPlanRules
                .Include(r => r.DayOfWeek)
                .Include(r => r.MealType)
                .Where(r => r.HouseholdId == householdId)
                .OrderBy(r => r.Id)
                .Select(rule => new MealPlanRuleResponseModel
                {
                    Id = rule.Id,
                    HouseholdId = rule.HouseholdId,
                    DayOfWeekId = rule.DayOfWeekId,
                    DayOfWeekName = rule.DayOfWeek != null ? rule.DayOfWeek.Name : null,
                    MealTypeId = rule.MealTypeId,
                    MealTypeName = rule.MealType != null ? rule.MealType.Name : null,
                    QueryFilter = rule.QueryFilter ?? string.Empty,
                    MaxRecipes = rule.MaxRecipes ?? 3,
                    IsActive = rule.IsActive,
                    CreatedDate = rule.CreatedDate,
                    ModifiedDate = rule.LastModifiedDate
                })
                .ToListAsync();
        }

        public async Task<bool> DeleteRuleAsync(long id)
        {
            var rule = await _context.MealPlanRules.FindAsync(id);
            if (rule == null)
                return false;

            _context.MealPlanRules.Remove(rule);
            await _context.SaveChangesAsync();
            return true;
        }

        // Default goal type name for nutrient guidelines (loaded dynamically by name)
        private const string DefaultGoalTypeName = "Adults and Children 4+ years";

        /// <summary>
        /// A per-nutrient scoring target for shuffle selection: USDA guideline
        /// values by default, overridden/extended by household macro goals.
        /// </summary>
        private sealed record GuidelineTarget(long NutrientId, string NutrientName, decimal Target, decimal? UpperLimit);

        /// <summary>
        /// Shuffle draws this many candidates per slot so scoring has a real choice; a pool the
        /// size of the slot count could only reorder a random set, never steer toward the goals.
        /// </summary>
        private const int PoolOversample = 3;

        /// <summary>Score lost per 100% relative miss of a macro's goal mix (grams per kcal) on a plate.</summary>
        private const decimal MacroMixWeight = 400m;

        /// <summary>
        /// A food with more alcohol than this per 100 g is never drawn by the food-group top-up as a
        /// standalone food: a spirit or wine admitted to Beverages is not a serving to schedule.
        /// </summary>
        internal const decimal MaxTopUpAlcoholGramsPer100g = 0.5m;

        // Meal composition templates: defines how many recipes of each type compose a meal
        private static readonly Dictionary<long, (long RecipeTypeId, string Label)[]> MealComposition = new()
        {
            [1100] = new[] { (3101L, "Entree"), (3103L, "Fruit/Vegetable") },       // Breakfast: 2
            [1101] = new[] { (3100L, "Appetizer"), (3101L, "Entree"), (3102L, "Starch") }, // Lunch: 3
            [1102] = new[] { (3100L, "Appetizer"), (3101L, "Entree"), (3102L, "Starch") }, // Dinner: 3
            [1103] = new[] { (3104L, "Snack") },                                    // Snacks: 1
            [DessertMealTypeId] = new[] { (3105L, "Dessert") },
        };

        /// <summary>
        /// Slot assignments must reference a recipe the author can SEE and that
        /// does not violate any LOCKED restriction of an active household
        /// member (design doc §2: plan-slot assignment of non-compliant
        /// recipes is rejected; hard-block is reserved for locks).
        /// Previously these writes had no validation at all.
        /// </summary>
        private async Task ValidateSlotAssignmentAsync(
            long householdId, long authorId, long? recipeId, long? ingredientId = null)
        {
            var isMember = await _context.HouseholdMembers
                .AnyAsync(hm => hm.HouseholdId == householdId && hm.PersonId == authorId && hm.IsActive);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not an active member of this household.");
            }

            if (recipeId.HasValue && ingredientId.HasValue)
            {
                throw new InvalidOperationException("A slot holds a recipe or a standalone food, not both.");
            }

            if (ingredientId.HasValue)
            {
                var ingredientExists = await _context.Ingredients.AnyAsync(i => i.Id == ingredientId.Value);
                if (!ingredientExists)
                {
                    throw new InvalidOperationException("restriction_violation:ingredient_not_found");
                }

                // A standalone food may not itself be a steward/provider-locked restriction.
                var lockedIds = await _policy.GetLockedIngredientIdsAsync(householdId);
                if (lockedIds.Contains(ingredientId.Value))
                {
                    throw new InvalidOperationException("restriction_violation:locked_restriction");
                }
                return;
            }

            if (!recipeId.HasValue)
            {
                return;
            }

            var visible = await _context.Recipes
                .VisibleTo(_context, authorId)
                .AnyAsync(r => r.Id == recipeId.Value);
            if (!visible)
            {
                throw new InvalidOperationException("restriction_violation:recipe_not_visible");
            }

            var lockedIngredientIds = await _policy.GetLockedIngredientIdsAsync(householdId);
            if (lockedIngredientIds.Count > 0)
            {
                var violates = await _context.Recipes
                    .Where(r => r.Id == recipeId.Value)
                    .AnyAsync(r => r.RecipeIngredients!.Any(ri => lockedIngredientIds.Contains(ri.IngredientId)));
                if (violates)
                {
                    // Machine-readable reason for the UI ("locked by your steward/provider").
                    throw new InvalidOperationException("restriction_violation:locked_restriction");
                }
            }
        }

        private static DateOnly WeekStartOf(DateOnly date) =>
            date.AddDays(-(((int)date.DayOfWeek + 6) % 7)); // Monday-start weeks

        public async Task<MealPlanShuffleResponseModel> ShuffleMealPlansAsync(MealPlanShuffleModel model, long authorId)
        {
            // Feature gate: a member whose policy gates "shuffle" cannot run it.
            if (await _policy.IsFeatureGatedAsync(authorId, model.HouseholdId, FeatureGateKeys.Shuffle))
            {
                throw new UnauthorizedAccessException("feature_gated:shuffle");
            }

            int deletedCount = 0;

            // The delete below used to commit on its own; a failure anywhere in the
            // rebuild then left the household with an emptied week. Delete + rebuild
            // now share one transaction (relational stores only — the in-memory test
            // provider has no transactions).
            var useTransaction = _context.Database.IsRelational();
            await using var transaction = useTransaction ? await _context.Database.BeginTransactionAsync() : null;

            // 1. If replacing, delete existing entries in the date range
            //    but preserve entries where shopping has already been completed
            if (model.ReplaceExisting)
            {
                var existing = await _context.MealPlans
                    .Where(mp => mp.HouseholdId == model.HouseholdId
                        && mp.Date >= model.StartDate
                        && mp.Date <= model.EndDate
                        && mp.ShoppingCompletedAt == null)
                    .ToListAsync();

                deletedCount = existing.Count;
                _context.MealPlans.RemoveRange(existing);
                await _context.SaveChangesAsync();
            }

            // 2. What the household's restrictions forbid, resolved through the
            //    restriction categories' criteria (a UI-saved "Nut Allergy" carries
            //    no IngredientId of its own — the criteria supply the ingredients).
            var restrictedSet = await new Support.HouseholdRestrictionResolver(_context).ResolveAsync(model.HouseholdId);
            var restrictedIngredientIds = restrictedSet.IngredientIds.ToList();

            // 2b. Safety gate: does any active member carry a SEVERE restriction
            //     (severity >= 4 — allergy/medical)? If so, recipes are limited
            //     to those built entirely from curated ingredients (below),
            //     because a non-curated ingredient's allergen/alias data can't
            //     be trusted and a restricted item could hide behind it.
            var hasSevereRestriction = restrictedSet.HasSevere;

            var activeMealTypes = await ActiveMealTypesAsync(model.HouseholdId);

            // 3. Determine which cells need filling
            var existingEntries = model.ReplaceExisting
                ? new List<MealPlanEntity>()
                : await _context.MealPlans
                    .Where(mp => mp.HouseholdId == model.HouseholdId
                        && mp.Date >= model.StartDate
                        && mp.Date <= model.EndDate)
                    .ToListAsync();

            var filledCells = new HashSet<(DateOnly Date, long MealTypeId)>(
                existingEntries.Select(e => (e.Date, e.MealTypeId)));

            // Build list of empty cells to fill
            var emptyCells = new List<(DateOnly Date, long MealTypeId)>();
            for (var date = model.StartDate; date <= model.EndDate; date = date.AddDays(1))
            {
                foreach (var mt in activeMealTypes)
                {
                    if (!filledCells.Contains((date, mt.Id)))
                    {
                        emptyCells.Add((date, mt.Id));
                    }
                }
            }

            var curatedOnly = await _policy.IsCuratedOnlyAsync(authorId, model.HouseholdId);
            var prepared = await LoadPreparedPoolsAsync(model.HouseholdId, emptyCells, restrictedSet, curatedOnly);

            // 4. Expand each empty cell into composition sub-slots
            var expanded = new List<(DateOnly Date, long MealTypeId, long RecipeTypeId)>();
            foreach (var cell in emptyCells)
            {
                if (prepared.Pools.ContainsKey(cell.MealTypeId))
                {
                    expanded.Add((cell.Date, cell.MealTypeId, PreparedSlotTypeId));
                }
                else if (MealComposition.TryGetValue(cell.MealTypeId, out var slots))
                {
                    foreach (var slot in slots)
                    {
                        expanded.Add((cell.Date, cell.MealTypeId, slot.RecipeTypeId));
                    }
                }
                else
                {
                    expanded.Add((cell.Date, cell.MealTypeId, 0));
                }
            }

            // 5. Group by (recipe type, meal type) and fetch random recipes per combination
            //    This ensures breakfast entrees are separate from dinner entrees, etc.
            var countByTypeAndMeal = expanded
                .Where(e => e.RecipeTypeId != PreparedSlotTypeId)
                .GroupBy(e => (e.RecipeTypeId, e.MealTypeId))
                .ToDictionary(g => g.Key, g => g.Count());

            var kitchen = new KitchenToolService(_context);
            var hideMissingTools = await kitchen.GetModeAsync(model.HouseholdId) == KitchenToolModes.Hide;
            var toolSet = hideMissingTools ? await kitchen.GetToolSetAsync() : null;
            var ownedTools = hideMissingTools ? await kitchen.GetOwnedToolIdsAsync(model.HouseholdId, toolSet) : null;

            async Task<List<RecipeEntity>> TakeRandomCookableAsync(IQueryable<RecipeEntity> source, int take)
            {
                var shuffled = source.OrderBy(r => EF.Functions.Random());
                if (!hideMissingTools) return await shuffled.Take(take).ToListAsync();

                var candidates = await shuffled
                    .Take(take * 4 + 10)
                    .Select(r => new
                    {
                        Recipe = r,
                        Linked = r.RecipeTools!.Select(t => new LinkedTool(t.ToolId, t.Source)).ToList(),
                        Steps = r.RecipeSteps!.Select(s => s.Summary + " " + s.Description).ToList(),
                    })
                    .ToListAsync();
                return candidates
                    .Where(c => KitchenToolEvaluator.Evaluate(
                        KitchenToolEvaluator.RequiredTools(c.Linked, c.Recipe.Name, c.Steps, toolSet), ownedTools!, toolSet).Fit != KitchenToolFit.Missing)
                    .Select(c => c.Recipe)
                    .Take(take)
                    .ToList();
            }

            var recipePools = new Dictionary<(long RecipeTypeId, long MealTypeId), List<RecipeEntity>>();
            foreach (var ((recipeTypeId, mealTypeId), count) in countByTypeAndMeal)
            {
                // Pool = recipes the shuffling member can SEE (public pool +
                // household + audience tiers). curated_only members draw only
                // from audience-scoped recipes and household cookbooks.
                var query = _context.Recipes.VisibleTo(_context, authorId);
                if (curatedOnly)
                {
                    query = query.Where(r =>
                        r.Visibility == RecipeVisibilityEnum.Audience
                        || _context.Set<Nom.Data.Plan.HouseholdCookbookRecipeEntity>().Any(cr =>
                            cr.RecipeId == r.Id && cr.HouseholdCookbook!.HouseholdId == model.HouseholdId));
                }

                query = Support.DietGateStore.WithoutHeld(query, _context, restrictedSet);

                if (hasSevereRestriction)
                {
                    // Conservative: every ingredient must be curated, so nothing
                    // with unverified allergen data reaches a severe-restriction household.
                    query = query.Where(r => r.RecipeIngredients!
                        .All(ri => ri.Ingredient.CurationStatusId == (long)CurationStatusEnum.Curated));
                }

                if (recipeTypeId != 0)
                {
                    query = query.Where(r => r.RecipeTypes!.Any(rt => rt.Id == recipeTypeId));
                }

                // Prefer recipes whose category matches the meal type (e.g., breakfast entrees for breakfast)
                var mealAffineRecipes = await TakeRandomCookableAsync(
                    query.Where(r => r.RecipeCategories!.Any(rc => rc.CategoryId == mealTypeId)), count * PoolOversample);

                // Fallback: if not enough meal-affine recipes, fill from the full pool
                if (mealAffineRecipes.Count < count)
                {
                    var existingIds = mealAffineRecipes.Select(r => r.Id).ToHashSet();
                    var fallback = await TakeRandomCookableAsync(
                        query.Where(r => !existingIds.Contains(r.Id)), count - mealAffineRecipes.Count);
                    mealAffineRecipes.AddRange(fallback);
                }

                recipePools[(recipeTypeId, mealTypeId)] = mealAffineRecipes;
            }

            // 5b. Load all nutrient guidelines for the default goal type (fully dynamic)
            var guidelines = (await _context.NutrientGuidelines
                .Where(ng => ng.GoalType.Name == DefaultGoalTypeName
                    && ng.RecommendedAmount != null)
                .Include(ng => ng.Nutrient)
                .Select(ng => new
                {
                    ng.NutrientId,
                    NutrientName = ng.Nutrient.Name,
                    Target = ng.RecommendedAmount!.Value,
                    UpperLimit = ng.MaxAmount,
                })
                .ToListAsync())
                .Select(g => new GuidelineTarget(g.NutrientId, g.NutrientName, g.Target, g.UpperLimit))
                .ToList();

            // 5b'. The members' macro goals (each person's own, falling back to
            // the household's) override the USDA defaults so shuffle scoring
            // steers toward the goals users actually set. A goal for a macro
            // with no guideline row (e.g. Calories has none in seed data) is
            // appended as a new scoring target rather than ignored.
            var macroGoal = await PlanningMacroGoal.LoadAsync(_context, model.HouseholdId);
            if (macroGoal.HasAnyTarget)
            {
                var goalTargets = new (string NutrientName, decimal? Target)[]
                {
                    ("Calories", macroGoal.CaloriesTarget),
                    ("Protein", macroGoal.ProteinGramsTarget),
                    ("Total Carbohydrates", macroGoal.CarbGramsTarget),
                    ("Fat", macroGoal.FatGramsTarget),
                };

                foreach (var (nutrientName, target) in goalTargets)
                {
                    if (!target.HasValue) continue;

                    var idx = guidelines.FindIndex(g => g.NutrientName == nutrientName);
                    if (idx >= 0)
                    {
                        guidelines[idx] = guidelines[idx] with { Target = target.Value };
                    }
                    else
                    {
                        var nutrientId = await _context.Nutrients
                            .Where(n => n.Name == nutrientName)
                            .Select(n => (long?)n.Id)
                            .FirstOrDefaultAsync();
                        if (nutrientId.HasValue)
                        {
                            guidelines.Add(new GuidelineTarget(nutrientId.Value, nutrientName, target.Value, null));
                        }
                    }
                }
            }

            var guidelinesByNutrient = guidelines.ToDictionary(g => g.NutrientId);
            var caloriesNutrientId = guidelines.FirstOrDefault(g => g.NutrientName == "Calories")?.NutrientId;

            var macroNutrientIds = await _context.Nutrients
                .Where(n => n.Name == "Calories" || n.Name == "Protein" || n.Name == "Total Carbohydrates" || n.Name == "Fat")
                .Select(n => new { n.Name, n.Id })
                .ToListAsync();
            var energyNutrientId = macroNutrientIds.FirstOrDefault(n => n.Name == "Calories")?.Id;
            var mixTargets = new List<(long NutrientId, decimal PerKcal)>();
            foreach (var (nutrientName, perKcal) in new (string, decimal?)[]
            {
                ("Protein", macroGoal.ProteinPerKcal),
                ("Total Carbohydrates", macroGoal.CarbPerKcal),
                ("Fat", macroGoal.FatPerKcal),
            })
            {
                var nutrient = macroNutrientIds.FirstOrDefault(n => n.Name == nutrientName);
                if (energyNutrientId.HasValue && nutrient != null && perKcal is > 0m)
                    mixTargets.Add((nutrient.Id, perKcal.Value));
            }
            var mealSplit = mixTargets.Count > 0
                ? await new PortionOrchestrationService(_context, NullLogger<PortionOrchestrationService>.Instance).GetMealSplitAsync(model.HouseholdId)
                : new Nom.Orch.Models.Plan.MealSplitModel();
            var cellPlates = new Dictionary<(DateOnly Date, long MealTypeId), Dictionary<long, decimal>>();

            decimal? MacroMixPenalty(DateOnly date, long mealTypeId, Dictionary<long, decimal>? candidate)
            {
                var share = PortionOrchestrationService.MealPct(mealSplit, mealTypeId);
                if (share <= 0m) return 0m;
                var candidateKcal = candidate?.GetValueOrDefault(energyNutrientId!.Value, 0m) ?? 0m;
                if (candidate == null || candidateKcal <= 0m) return null;

                cellPlates.TryGetValue((date, mealTypeId), out var plate);
                var plateKcal = (plate?.GetValueOrDefault(energyNutrientId.Value, 0m) ?? 0m) + candidateKcal;
                var otherCells = cellPlates
                    .Where(c => c.Key.Date == date && c.Key.MealTypeId != mealTypeId
                        && c.Value.GetValueOrDefault(energyNutrientId.Value, 0m) > 0m)
                    .Select(c => (Share: PortionOrchestrationService.MealPct(mealSplit, c.Key.MealTypeId), Plate: c.Value))
                    .ToList();
                var otherShare = otherCells.Sum(c => c.Share);

                var penalty = 0m;
                foreach (var (nutrientId, perKcal) in mixTargets)
                {
                    var otherGrams = otherCells.Sum(c =>
                        c.Share * c.Plate.GetValueOrDefault(nutrientId, 0m) / c.Plate[energyNutrientId.Value]);
                    var wanted = Math.Max(0m, (perKcal * (otherShare + share) - otherGrams) / share);
                    var actual = ((plate?.GetValueOrDefault(nutrientId, 0m) ?? 0m) + candidate.GetValueOrDefault(nutrientId, 0m)) / plateKcal;
                    penalty += MacroMixWeight * Math.Abs(actual - wanted) / perKcal;
                }
                return penalty;
            }

            var trackedNutrientIds = guidelines.Select(g => g.NutrientId)
                .Concat(mixTargets.Select(t => t.NutrientId))
                .Concat(mixTargets.Count > 0 ? new[] { energyNutrientId!.Value } : Array.Empty<long>())
                .Distinct()
                .ToList();

            // 5c. Load nutrition data for all candidate recipes (all guideline-tracked nutrients)
            var allCandidateIds = recipePools.Values
                .SelectMany(pool => pool.Select(r => r.Id))
                .Distinct()
                .ToList();

            var recipeNutritionData = await _context.RecipeNutrition
                .Where(rn => trackedNutrientIds.Contains(rn.NutrientId)
                    && allCandidateIds.Contains(rn.RecipeId))
                .ToListAsync();

            // Build lookup: recipeId → { nutrientId → amount }
            var recipeNutrients = recipeNutritionData
                .GroupBy(rn => rn.RecipeId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(rn => rn.NutrientId, rn => rn.Amount));

            // 6. Assign recipes to slots with nutrient-aware scoring
            //    Dynamically tracks all nutrients that have guidelines in NutrientGuidelines.
            //    Uses MaxAmount (when set) as a hard ceiling with heavier overshoot penalty.
            //    Uses RecommendedAmount as the target for all nutrients.
            // Frequency caps (member policies): strictest cap per tag across
            // the household's members shapes placement — a capped tag stops
            // being placed once its week bucket is full. Manual edits only
            // warn (UI); shuffle hard-skips.
            var capPerTag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var capsJson in await _context.MemberPolicies
                .Where(mp => mp.HouseholdId == model.HouseholdId)
                .Select(mp => mp.FrequencyCaps)
                .ToListAsync())
            {
                if (string.IsNullOrWhiteSpace(capsJson)) continue;
                try
                {
                    var caps = System.Text.Json.JsonSerializer.Deserialize<List<Nom.Orch.Models.Policy.FrequencyCapModel>>(
                        capsJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                    foreach (var cap in caps ?? new List<Nom.Orch.Models.Policy.FrequencyCapModel>())
                    {
                        if (string.IsNullOrWhiteSpace(cap.Tag) || cap.MaxPerWeek < 0) continue;
                        capPerTag[cap.Tag] = capPerTag.TryGetValue(cap.Tag, out var existingCap)
                            ? Math.Min(existingCap, cap.MaxPerWeek)
                            : cap.MaxPerWeek;
                    }
                }
                catch (System.Text.Json.JsonException) { /* malformed caps never break shuffle */ }
            }

            var recipeTagNames = new Dictionary<long, List<string>>();
            var weekTagCounts = new Dictionary<(DateOnly WeekStart, string Tag), int>();
            if (capPerTag.Count > 0)
            {
                var candidateIds = recipePools.Values.SelectMany(pool => pool.Select(r => r.Id)).Distinct().ToList();
                var tagRows = await _context.Set<RecipeTagEntity>()
                    .Where(rt => candidateIds.Contains(rt.RecipeId) && rt.Tag != null)
                    .Select(rt => new { rt.RecipeId, TagName = rt.Tag!.Name })
                    .ToListAsync();
                foreach (var row in tagRows)
                {
                    if (!recipeTagNames.TryGetValue(row.RecipeId, out var list))
                    {
                        recipeTagNames[row.RecipeId] = list = new List<string>();
                    }
                    list.Add(row.TagName);
                }

                // Seed week buckets from surviving existing entries in range.
                var existingWithRecipes = await _context.MealPlans
                    .Where(mp => mp.HouseholdId == model.HouseholdId
                        && mp.Date >= model.StartDate && mp.Date <= model.EndDate && mp.RecipeId != null)
                    .Select(mp => new { mp.Date, RecipeId = mp.RecipeId!.Value })
                    .ToListAsync();
                var existingRecipeIds = existingWithRecipes.Select(e => e.RecipeId).Distinct().ToList();
                var existingTags = await _context.Set<RecipeTagEntity>()
                    .Where(rt => existingRecipeIds.Contains(rt.RecipeId) && rt.Tag != null)
                    .Select(rt => new { rt.RecipeId, TagName = rt.Tag!.Name })
                    .ToListAsync();
                var existingTagLookup = existingTags.GroupBy(t => t.RecipeId)
                    .ToDictionary(g => g.Key, g => g.Select(t => t.TagName).ToList());
                foreach (var entry in existingWithRecipes)
                {
                    foreach (var tag in existingTagLookup.GetValueOrDefault(entry.RecipeId) ?? new List<string>())
                    {
                        if (!capPerTag.ContainsKey(tag)) continue;
                        var key = (WeekStartOf(entry.Date), tag);
                        weekTagCounts[key] = weekTagCounts.GetValueOrDefault(key) + 1;
                    }
                }
            }

            var usedIds = new HashSet<long>();
            var usedPreparedIds = new HashSet<long>();
            var poolCounters = new Dictionary<(long, long), int>();
            var newEntities = new List<MealPlanEntity>();
            var dailyTotals = new Dictionary<DateOnly, Dictionary<long, decimal>>();
            var now = DateTime.UtcNow;

            decimal Score(DateOnly date, long mealTypeId, Dictionary<long, decimal> dayTotals,
                Dictionary<long, decimal>? nutrients, bool isUnused, decimal unknownMixPenalty)
            {
                var score = 0m;
                if (isUnused) score += 1000m;

                // Score every nutrient that has a guideline
                foreach (var guideline in guidelines)
                {
                    var candidateAmount = nutrients?.GetValueOrDefault(guideline.NutrientId, 0m) ?? 0m;
                    if (candidateAmount == 0m) continue;

                    var currentTotal = dayTotals.GetValueOrDefault(guideline.NutrientId, 0m);
                    var remaining = guideline.Target - currentTotal;
                    // Heavier penalty when MaxAmount is set (hard ceiling like sodium)
                    var overshootWeight = guideline.UpperLimit.HasValue ? 3m : 1.5m;

                    if (remaining > 0 && candidateAmount <= remaining)
                    {
                        // Contributes toward target without exceeding — reward
                        score += 100m * candidateAmount / guideline.Target;
                    }
                    else if (remaining > 0)
                    {
                        // Partially contributes but overshoots — partial reward, proportional penalty
                        score += 100m * remaining / guideline.Target;
                        score -= overshootWeight * 100m * (candidateAmount - remaining) / guideline.Target;
                    }
                    else
                    {
                        // Target already met — penalize further addition
                        score -= overshootWeight * 50m * candidateAmount / guideline.Target;
                    }
                }

                // Calorie distribution: prefer even spread across remaining slots
                if (caloriesNutrientId.HasValue && nutrients != null)
                {
                    var calAmount = nutrients.GetValueOrDefault(caloriesNutrientId.Value, 0m);
                    if (calAmount > 0 && guidelinesByNutrient.TryGetValue(caloriesNutrientId.Value, out var calGuideline))
                    {
                        var calRemaining = calGuideline.Target - dayTotals.GetValueOrDefault(caloriesNutrientId.Value, 0m);
                        if (calRemaining > 0)
                            score -= Math.Abs(calAmount - calRemaining / 3m) * 0.5m;
                    }
                }

                if (mixTargets.Count > 0)
                    score -= MacroMixPenalty(date, mealTypeId, nutrients) ?? unknownMixPenalty;

                return score;
            }

            void AddToTotals(DateOnly date, long mealTypeId, Dictionary<long, decimal> dayTotals, Dictionary<long, decimal> nutrients)
            {
                if (!cellPlates.TryGetValue((date, mealTypeId), out var cellPlate))
                {
                    cellPlates[(date, mealTypeId)] = cellPlate = new Dictionary<long, decimal>();
                }
                foreach (var (nutrientId, amount) in nutrients)
                {
                    dayTotals[nutrientId] = dayTotals.GetValueOrDefault(nutrientId, 0m) + amount;
                    cellPlate[nutrientId] = cellPlate.GetValueOrDefault(nutrientId, 0m) + amount;
                }
            }

            foreach (var slot in expanded.OrderBy(s => s.Date).ThenBy(s => s.MealTypeId))
            {
                if (!dailyTotals.TryGetValue(slot.Date, out var dayTotals))
                {
                    dayTotals = new Dictionary<long, decimal>();
                    dailyTotals[slot.Date] = dayTotals;
                }

                if (slot.RecipeTypeId == PreparedSlotTypeId)
                {
                    var preparedPool = prepared.Pools[slot.MealTypeId];
                    var unknownPreparedPenalty = mixTargets.Count > 0
                        ? preparedPool.Select(c => MacroMixPenalty(slot.Date, slot.MealTypeId, c.Nutrients))
                            .Where(p => p.HasValue).Select(p => p!.Value).DefaultIfEmpty(0m).Max()
                        : 0m;
                    var meal = preparedPool
                        .Select(c => (Candidate: c, Score: Score(slot.Date, slot.MealTypeId, dayTotals,
                            c.Nutrients, !usedPreparedIds.Contains(c.Id), unknownPreparedPenalty)))
                        .OrderByDescending(c => c.Score)
                        .First().Candidate;

                    usedPreparedIds.Add(meal.Id);
                    AddToTotals(slot.Date, slot.MealTypeId, dayTotals, meal.Nutrients);
                    newEntities.Add(new MealPlanEntity
                    {
                        HouseholdId = model.HouseholdId,
                        AuthorId = authorId,
                        Date = slot.Date,
                        MealTypeId = slot.MealTypeId,
                        Title = meal.Name,
                        IngredientId = meal.Id,
                        Quantity = 1m,
                        CreatedDate = now,
                        LastModifiedDate = now,
                    });
                    continue;
                }

                var poolKey = (slot.RecipeTypeId, slot.MealTypeId);
                if (!recipePools.TryGetValue(poolKey, out var pool) || pool.Count == 0)
                    continue;

                var unknownMixPenalty = mixTargets.Count > 0
                    ? pool.Select(c => MacroMixPenalty(slot.Date, slot.MealTypeId, recipeNutrients.GetValueOrDefault(c.Id)))
                        .Where(p => p.HasValue).Select(p => p!.Value).DefaultIfEmpty(0m).Max()
                    : 0m;

                RecipeEntity? bestRecipe = null;
                var bestScore = decimal.MinValue;

                foreach (var candidate in pool)
                {
                    // Frequency caps: hard-skip a candidate whose capped tag is
                    // already at its weekly limit for this slot's week.
                    if (capPerTag.Count > 0
                        && recipeTagNames.TryGetValue(candidate.Id, out var candidateTags)
                        && candidateTags.Any(t => capPerTag.TryGetValue(t, out var cap)
                            && weekTagCounts.GetValueOrDefault((WeekStartOf(slot.Date), t)) >= cap))
                    {
                        continue;
                    }

                    recipeNutrients.TryGetValue(candidate.Id, out var nutrients);
                    var score = Score(slot.Date, slot.MealTypeId, dayTotals, nutrients, !usedIds.Contains(candidate.Id), unknownMixPenalty);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRecipe = candidate;
                    }
                }

                // Fallback: cycle through pool if scoring found no candidate
                if (bestRecipe == null)
                {
                    poolCounters.TryGetValue(poolKey, out var idx);
                    bestRecipe = pool[idx % pool.Count];
                    poolCounters[poolKey] = idx + 1;
                }

                if (capPerTag.Count > 0
                    && recipeTagNames.TryGetValue(bestRecipe.Id, out var placedTags))
                {
                    foreach (var tag in placedTags)
                    {
                        if (!capPerTag.ContainsKey(tag)) continue;
                        var key = (WeekStartOf(slot.Date), tag);
                        weekTagCounts[key] = weekTagCounts.GetValueOrDefault(key) + 1;
                    }
                }

                usedIds.Add(bestRecipe.Id);

                // Update daily totals for all tracked nutrients
                if (recipeNutrients.TryGetValue(bestRecipe.Id, out var selectedNutrients))
                {
                    AddToTotals(slot.Date, slot.MealTypeId, dayTotals, selectedNutrients);
                }

                newEntities.Add(new MealPlanEntity
                {
                    HouseholdId = model.HouseholdId,
                    AuthorId = authorId,
                    Date = slot.Date,
                    MealTypeId = slot.MealTypeId,
                    Title = bestRecipe.Name,
                    RecipeId = bestRecipe.Id,
                    CreatedDate = now,
                    LastModifiedDate = now,
                });
            }

            // 6b. Food-group requirements: guarantee each active household rule's minimum
            // by counting servings already supplied by placed recipes + standalone items,
            // then topping up with standalone whole foods.
            var topUps = await ApplyFoodGroupRulesAsync(
                model.HouseholdId, authorId, model.StartDate, model.EndDate,
                newEntities, restrictedIngredientIds, now, restrictedSet);
            newEntities.AddRange(topUps);

            // 7. Bulk insert
            _context.MealPlans.AddRange(newEntities);
            await _context.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            // 8. Return the week view for the date range
            var weekStart = model.StartDate;
            // Align to Monday for GetWeekAsync
            var dayOfWeek = weekStart.DayOfWeek;
            var mondayOffset = dayOfWeek == DayOfWeek.Sunday ? -6 : (int)DayOfWeek.Monday - (int)dayOfWeek;
            var monday = weekStart.AddDays(mondayOffset);

            var week = await GetWeekAsync(model.HouseholdId, monday);

            return new MealPlanShuffleResponseModel
            {
                Created = newEntities.Count,
                Deleted = deletedCount,
                Week = week,
                PreparedMealsHeld = prepared.Held,
            };
        }

        /// <summary>The composition sub-slot of a meal the household has given to prepared meals.</summary>
        internal const long PreparedSlotTypeId = -1L;

        /// <summary>A prepared food the generator may serve as a meal, with its nutrients for one reference serving.</summary>
        private sealed record PreparedMealCandidate(long Id, string Name, Dictionary<long, decimal> Nutrients);

        /// <summary>The prepared-meal pools per meal type, and why prepared meals were held back when they were.</summary>
        private sealed record PreparedPools(Dictionary<long, List<PreparedMealCandidate>> Pools, string? Held);

        /// <summary>Why shuffle placed no prepared meal for a meal the household gave to them.</summary>
        public static class PreparedMealsHeldReasons
        {
            /// <summary>A member has an allergy or medical restriction, and no prepared meal of the meal has contents read from an authoritative statement that show it is safe.</summary>
            public const string SevereRestriction = "severe_restriction";

            /// <summary>The member who shuffled draws only from audience and cookbook recipes.</summary>
            public const string CuratedOnly = "curated_only";

            /// <summary>A member has a restriction or diet judged by what a dish contains (a pattern, or a food that is not a prepared food), and no prepared meal of the meal has contents read from an authoritative statement that show it fits.</summary>
            public const string DietRestriction = "diet_restriction";

            /// <summary>No curated prepared meal passes the household's restrictions, diets and locks.</summary>
            public const string NoneAvailable = "none_available";
        }

        /// <summary>
        /// The prepared meals shuffle may place in the empty cells of the meals the household gave to prepared meals
        /// (<see cref="PortionOrchestrationService.PreparedMealsPreferenceKey"/>): curated prepared meals
        /// (<see cref="Nom.Data.Nutrition.PreparedFoods.Meals"/>) that no restriction, diet or steward/provider lock of the
        /// household forbids, drawn at random, <see cref="PoolOversample"/> per cell, each with its nutrients for one
        /// reference serving (100 g when it has none), the basis the week view shows. When a member has a severe
        /// restriction or a restriction or diet judged by what a dish contains (<see cref="Support.RestrictedIngredientSet.ReadsContents"/>),
        /// only meals whose contents were read from an authoritative statement and pass the same gate as a recipe's lines
        /// are drawn (<see cref="Support.PreparedContentsGate"/>); a meal none of whose prepared meals passes falls back to
        /// recipes and says why. None when the member who shuffles is curated-only.
        /// </summary>
        private async Task<PreparedPools> LoadPreparedPoolsAsync(
            long householdId, List<(DateOnly Date, long MealTypeId)> emptyCells, Support.RestrictedIngredientSet restricted, bool curatedOnly)
        {
            var pools = new Dictionary<long, List<PreparedMealCandidate>>();
            var stored = await _context.HouseholdPreferences
                .AsNoTracking()
                .Where(p => p.HouseholdId == householdId && p.PreferenceKey == PortionOrchestrationService.PreparedMealsPreferenceKey)
                .Select(p => p.PreferenceValue)
                .FirstOrDefaultAsync();
            var cellsByMeal = PortionOrchestrationService.ParsePreparedMealTypeIds(stored)
                .Select(mealTypeId => (MealTypeId: mealTypeId, Cells: emptyCells.Count(c => c.MealTypeId == mealTypeId)))
                .Where(m => m.Cells > 0)
                .ToList();
            if (cellsByMeal.Count == 0) return new PreparedPools(pools, null);
            var byContents = restricted.HasSevere || restricted.ReadsContents;
            var contentsHeld = restricted.HasSevere ? PreparedMealsHeldReasons.SevereRestriction : PreparedMealsHeldReasons.DietRestriction;
            if (curatedOnly) return new PreparedPools(pools, byContents ? contentsHeld : PreparedMealsHeldReasons.CuratedOnly);

            var excluded = restricted.IngredientIds.Concat(await _policy.GetLockedIngredientIdsAsync(householdId)).Distinct().ToList();
            foreach (var (mealTypeId, cells) in cellsByMeal)
            {
                var query = PreparedMealPoolQuery(_context, householdId, excluded, byContents, restricted);
                List<(long Id, string Name, decimal? ReferenceServingGrams)> foods;
                if (byContents)
                {
                    var known = (await query
                            .Select(i => new { i.Id, i.Name, i.ReferenceServingGrams })
                            .ToListAsync())
                        .Select(f => (f.Id, f.Name, f.ReferenceServingGrams))
                        .ToList();
                    var verdicts = await Support.PreparedContentsGate.JudgeAsync(_context, householdId, restricted, known.Select(f => f.Id).ToList());
                    foods = known.Where(f => verdicts[f.Id].Offered).OrderBy(_ => Random.Shared.Next()).Take(cells * PoolOversample).ToList();
                }
                else
                {
                    foods = (await query
                            .OrderBy(i => EF.Functions.Random())
                            .Take(cells * PoolOversample)
                            .Select(i => new { i.Id, i.Name, i.ReferenceServingGrams })
                            .ToListAsync())
                        .Select(f => (f.Id, f.Name, f.ReferenceServingGrams))
                        .ToList();
                }
                if (foods.Count == 0) continue;

                var ids = foods.Select(f => f.Id).ToList();
                var nutrientRows = await _context.Set<Nom.Data.Nutrient.IngredientNutrientEntity>()
                    .Where(n => ids.Contains(n.IngredientId) && !n.IsDeleted)
                    .Select(n => new { n.IngredientId, n.NutrientId, n.Amount })
                    .ToListAsync();
                pools[mealTypeId] = foods.Select(f =>
                {
                    var basis = f.ReferenceServingGrams is > 0 ? f.ReferenceServingGrams.Value : 100m;
                    var nutrients = nutrientRows.Where(n => n.IngredientId == f.Id)
                        .GroupBy(n => n.NutrientId)
                        .ToDictionary(g => g.Key, g => g.First().Amount * basis / 100m);
                    return new PreparedMealCandidate(f.Id, f.Name, nutrients);
                }).ToList();
            }

            var held = pools.Count < cellsByMeal.Count ? (byContents ? contentsHeld : PreparedMealsHeldReasons.NoneAvailable) : null;
            return new PreparedPools(pools, held);
        }

        /// <summary>
        /// The curated prepared meals shuffle may draw, less the excluded foods (restricted or locked); with
        /// <paramref name="withReadContents"/>, only those with a read statement of FDC or of the household; with
        /// <paramref name="restricted"/>, none the diet gate's stored sets count or hold (<see cref="Support.DietGateStore.WithoutHeld(IQueryable{IngredientEntity}, ApplicationDbContext, Support.RestrictedIngredientSet)"/>).
        /// </summary>
        internal static IQueryable<IngredientEntity> PreparedMealPoolQuery(ApplicationDbContext db, long householdId, IReadOnlyCollection<long> excluded, bool withReadContents, Support.RestrictedIngredientSet? restricted = null)
        {
            var read = Nom.Data.Recipe.IngredientContentsOutcome.Read;
            var query = db.Ingredients
                .Where(i => i.CurationStatusId == (long)CurationStatusEnum.Curated)
                .Where(Nom.Data.Nutrition.PreparedFoods.Meals)
                .Where(i => !excluded.Contains(i.Id));
            if (restricted != null) query = Support.DietGateStore.WithoutHeld(query, db, restricted);
            return withReadContents
                ? query.Where(i => db.IngredientContents.Any(c => c.IngredientId == i.Id && c.Outcome == read && (c.HouseholdId == null || c.HouseholdId == householdId)))
                : query;
        }

        /// <summary>
        /// Whether each prepared meal may be offered to the household, judged as shuffle judges it: a food the household's
        /// restrictions name is held; with a severe restriction or one judged by contents, the contents gate decides
        /// (<see cref="Support.PreparedContentsGate"/>); otherwise it is offered. Foods that are not prepared foods are left out.
        /// A held meal is marked severe when the household has a restriction of severity 4 or 5 (allergy or medical), so the
        /// picker asks for an explicit "Add anyway" before a member adds it by hand; for a diet the reason is only shown.
        /// </summary>
        public async Task<List<PreparedMealCheckModel>> CheckPreparedMealsAsync(long householdId, IReadOnlyCollection<long> ingredientIds)
        {
            var ids = ingredientIds.Distinct().Take(MaxPreparedChecks).ToList();
            if (ids.Count == 0) return new List<PreparedMealCheckModel>();
            var prepared = await _context.Ingredients.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Where(Nom.Data.Nutrition.PreparedFoods.Prepared)
                .Select(i => i.Id)
                .ToListAsync();
            if (prepared.Count == 0) return new List<PreparedMealCheckModel>();

            var restricted = await new Support.HouseholdRestrictionResolver(_context).ResolveAsync(householdId);
            if (!restricted.HasSevere && !restricted.ReadsContents)
            {
                await restricted.LoadFoodsAsync(_context, prepared);
                var undecided = await Support.DietGateStore.UndecidedAsync(_context, restricted, prepared);
                return prepared.Select(id => restricted.IngredientIds.Contains(id)
                    ? new PreparedMealCheckModel { IngredientId = id, Held = Support.PreparedContentsHeld.Food }
                    : restricted.Pending || undecided.Contains(id)
                        ? new PreparedMealCheckModel { IngredientId = id, Held = Support.PreparedContentsHeld.Pending }
                        : new PreparedMealCheckModel { IngredientId = id }).ToList();
            }

            var verdicts = await Support.PreparedContentsGate.JudgeAsync(_context, householdId, restricted, prepared);
            return prepared.Select(id => new PreparedMealCheckModel
            {
                IngredientId = id,
                Held = verdicts[id].Held,
                Names = verdicts[id].Names.ToList(),
                Severe = restricted.HasSevere && verdicts[id].Held != null,
            }).ToList();
        }

        /// <summary>The most foods one prepared-meal check judges.</summary>
        public const int MaxPreparedChecks = 50;

        /// <summary>
        /// Guarantees each active household <see cref="FoodGroupRuleEntity"/> minimum over the shuffled
        /// range. A serving of a food group is supplied by any placed recipe that contains an ingredient of
        /// that group, or by a standalone whole food of that group; a prepared food never supplies one.
        /// Shortfalls are topped up by scheduling standalone curated whole foods (respecting restrictions
        /// and steward/provider locks), never a prepared food, a food marked as a recipe-only ingredient
        /// or one with alcohol above <see cref="MaxTopUpAlcoholGramsPer100g"/>. Returns the
        /// standalone entries to add; never mutates <paramref name="placed"/>.
        /// </summary>
        internal async Task<List<MealPlanEntity>> ApplyFoodGroupRulesAsync(
            long householdId, long authorId, DateOnly startDate, DateOnly endDate,
            List<MealPlanEntity> placed, List<long> restrictedIngredientIds, DateTime now, Support.RestrictedIngredientSet? restricted = null)
        {
            const int MaxTopUpsPerBucket = 12; // safety cap against pathological rules

            var rules = await _context.FoodGroupRules
                .Where(r => r.HouseholdId == householdId && r.IsActive)
                .ToListAsync();
            if (rules.Count == 0) return new List<MealPlanEntity>();

            var lockedIds = await _policy.GetLockedIngredientIdsAsync(householdId);
            var groupIds = rules.Select(r => r.FoodGroupId).Distinct().ToList();

            // Recipe -> the food groups it supplies (contains an ingredient of that group).
            var placedRecipeIds = placed.Where(p => p.RecipeId.HasValue)
                .Select(p => p.RecipeId!.Value).Distinct().ToList();
            var recipeGroups = new Dictionary<long, HashSet<long>>();
            if (placedRecipeIds.Count > 0)
            {
                var rows = await _context.Recipes
                    .Where(r => placedRecipeIds.Contains(r.Id))
                    .Select(r => new
                    {
                        r.Id,
                        Groups = r.RecipeIngredients!
                            .Where(ri => ri.Ingredient!.FoodGroupId != null
                                && groupIds.Contains(ri.Ingredient.FoodGroupId!.Value)
                                && (ri.Ingredient.FdcCategoryId == null || !Nom.Data.Nutrition.PreparedFoods.CategoryIds.Contains(ri.Ingredient.FdcCategoryId.Value))
                                && !(ri.Ingredient.FdcDataType == Nom.Data.Nutrition.CatalogAdmissionRule.BrandedFood && ri.Ingredient.FdcCategory != null && Nom.Data.Nutrition.PreparedFoods.BrandedMealCategories.Contains(ri.Ingredient.FdcCategory)))
                            .Select(ri => ri.Ingredient!.FoodGroupId!.Value)
                            .Distinct().ToList()
                    })
                    .ToListAsync();
                foreach (var row in rows)
                    recipeGroups[row.Id] = row.Groups.ToHashSet();
            }

            // Candidate standalone whole foods per group (curated, not restricted, not locked).
            var candidatesByGroup = new Dictionary<long, List<(long Id, string Name)>>();
            var candidateGroupOf = new Dictionary<long, long>();
            foreach (var gid in groupIds)
            {
                var candidates = _context.Ingredients
                    .Where(i => i.FoodGroupId == gid
                        && i.CurationStatusId == (long)CurationStatusEnum.Curated
                        && i.IsWholeFood != false
                        && (i.FdcCategoryId == null || !Nom.Data.Nutrition.PreparedFoods.CategoryIds.Contains(i.FdcCategoryId.Value))
                        && !(i.FdcDataType == Nom.Data.Nutrition.CatalogAdmissionRule.BrandedFood && i.FdcCategory != null && Nom.Data.Nutrition.PreparedFoods.BrandedMealCategories.Contains(i.FdcCategory))
                        && !i.IngredientNutrients.Any(n => !n.IsDeleted && n.Nutrient.Name.ToLower() == "alcohol" && n.Amount > MaxTopUpAlcoholGramsPer100g)
                        && !restrictedIngredientIds.Contains(i.Id)
                        && !lockedIds.Contains(i.Id));
                if (restricted != null) candidates = Support.DietGateStore.WithoutHeld(candidates, _context, restricted);
                var cands = await candidates
                    .OrderBy(i => i.Id)
                    .Select(i => new { i.Id, i.Name })
                    .Take(10)
                    .ToListAsync();
                candidatesByGroup[gid] = cands.Select(c => (c.Id, c.Name)).ToList();
                foreach (var c in cands) candidateGroupOf[c.Id] = gid;
            }

            var additions = new List<MealPlanEntity>();
            var rr = new Dictionary<long, int>(); // round-robin cursor per group

            decimal CountGroup(IEnumerable<long> countMeals, long groupId, DateOnly date)
            {
                decimal total = 0m;
                var meals = countMeals.ToHashSet();
                foreach (var e in placed.Concat(additions))
                {
                    if (e.Date != date || !meals.Contains(e.MealTypeId)) continue;
                    if (e.RecipeId.HasValue
                        && recipeGroups.TryGetValue(e.RecipeId.Value, out var gs) && gs.Contains(groupId))
                        total += 1m;
                    else if (e.IngredientId.HasValue
                        && candidateGroupOf.TryGetValue(e.IngredientId.Value, out var sg) && sg == groupId)
                        total += e.Quantity ?? 1m;
                }
                return total;
            }

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                foreach (var rule in rules)
                {
                    // Determine the counting/placement buckets for this rule.
                    var buckets = new List<(List<long> CountMeals, long PlaceMeal)>();
                    if (rule.MealTypeId.HasValue)
                    {
                        buckets.Add((new List<long> { rule.MealTypeId.Value }, rule.MealTypeId.Value));
                    }
                    else if (rule.Timeframe == FoodGroupRuleTimeframe.PerMeal)
                    {
                        foreach (var mt in MealTypes)
                            buckets.Add((new List<long> { mt.Id }, mt.Id));
                    }
                    else // PerDay, all meals
                    {
                        buckets.Add((MealTypes.Select(m => m.Id).ToList(), 1103L /* Snacks */));
                    }

                    var cands = candidatesByGroup.GetValueOrDefault(rule.FoodGroupId);
                    foreach (var (countMeals, placeMeal) in buckets)
                    {
                        int added = 0;
                        while (CountGroup(countMeals, rule.FoodGroupId, date) < rule.MinServings
                            && added < MaxTopUpsPerBucket)
                        {
                            if (cands == null || cands.Count == 0) break; // nothing classified to add
                            var cursor = rr.GetValueOrDefault(rule.FoodGroupId);
                            var pick = cands[cursor % cands.Count];
                            rr[rule.FoodGroupId] = cursor + 1;

                            additions.Add(new MealPlanEntity
                            {
                                HouseholdId = householdId,
                                AuthorId = authorId,
                                Date = date,
                                MealTypeId = placeMeal,
                                IngredientId = pick.Id,
                                Quantity = 1m,
                                Title = pick.Name,
                                CreatedDate = now,
                                LastModifiedDate = now,
                            });
                            added++;
                        }
                    }
                }
            }

            return additions;
        }

        // Meal type IDs from reference data seed
        public const long DessertMealTypeId = 1104L;

        private async Task<(long Id, string Name)[]> ActiveMealTypesAsync(long householdId)
        {
            var includeDessert = await _context.HouseholdPreferences
                .AsNoTracking()
                .AnyAsync(p => p.HouseholdId == householdId
                    && p.PreferenceKey == PortionOrchestrationService.IncludeDessertPreferenceKey
                    && p.PreferenceValue == "true");
            return includeDessert ? MealTypes.Append((DessertMealTypeId, "Dessert")).ToArray() : MealTypes;
        }

        private static readonly (long Id, string Name)[] MealTypes = new[]
        {
            (1100L, "Breakfast"),
            (1101L, "Lunch"),
            (1102L, "Dinner"),
            (1103L, "Snacks")
        };

        // Common nutrient name patterns for macro extraction
        private static readonly string[] CalorieNames = { "energy", "calories", "kcal" };
        private static readonly string[] ProteinNames = { "protein" };
        private static readonly string[] CarbNames = { "carbohydrate", "carbs", "total carbohydrate" };
        private static readonly string[] FatNames = { "total lipid", "fat", "total fat" };

        public async Task<MealPlanWeekResponseModel> GetWeekAsync(long householdId, DateOnly weekStart)
        {
            var weekEnd = weekStart.AddDays(6);

            var mealPlans = await _context.MealPlans
                .Include(mp => mp.Recipe)
                    .ThenInclude(r => r!.Nutrition!)
                        .ThenInclude(n => n.Nutrient)
                .Include(mp => mp.MealType)
                .Include(mp => mp.Ingredient)
                    .ThenInclude(i => i!.FoodGroup)
                .Include(mp => mp.Ingredient)
                    .ThenInclude(i => i!.IngredientNutrients)
                        .ThenInclude(inut => inut.Nutrient)
                .Include(mp => mp.Measurement)
                .Where(mp => mp.HouseholdId == householdId && mp.Date >= weekStart && mp.Date <= weekEnd)
                .ToListAsync();

            var exclusions = await _context.MealPlanExclusions
                .Include(e => e.Person)
                .Include(e => e.MealType)
                .Where(e => e.HouseholdId == householdId && e.Date >= weekStart && e.Date <= weekEnd)
                .ToListAsync();

            var activeMemberIds = mealPlans.Any(mp => mp.Ingredient != null && Nom.Data.Nutrition.PreparedFoods.IsPrepared(mp.Ingredient))
                ? (await _context.HouseholdMembers
                    .Where(hm => hm.HouseholdId == householdId && hm.IsActive)
                    .Select(hm => hm.PersonId)
                    .ToListAsync()).ToHashSet()
                : new HashSet<long>();

            decimal Eaters(DateOnly date, long mealTypeId) => activeMemberIds.Count - exclusions
                .Where(x => x.Date == date && (x.MealTypeId == null || x.MealTypeId == mealTypeId) && activeMemberIds.Contains(x.PersonId))
                .Select(x => x.PersonId)
                .Distinct()
                .Count();

            var activeMealTypes = await ActiveMealTypesAsync(householdId);
            var removedRecipes = await RemovedAsync(mealPlans);
            var days = new List<MealPlanDayModel>();
            for (int i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                var dayMealPlans = mealPlans.Where(mp => mp.Date == date).ToList();
                var dayExclusions = exclusions.Where(e => e.Date == date).ToList();

                var cells = activeMealTypes.Select(mt =>
                {
                    var slotEntries = dayMealPlans.Where(mp => mp.MealTypeId == mt.Id).ToList();
                    var entryModels = slotEntries.Select(e =>
                    {
                        var isPrepared = e.Ingredient != null && Nom.Data.Nutrition.PreparedFoods.IsPrepared(e.Ingredient);
                        var gone = e.RecipeId is { } recipeId && removedRecipes.TryGetValue(recipeId, out var r) ? r : null;
                        var entryModel = new MealPlanEntryModel
                        {
                            Id = e.Id,
                            RecipeId = e.RecipeId,
                            RecipeName = gone?.Title ?? e.Recipe?.Name,
                            RecipeRemoved = gone != null,
                            RecipeSourceUrl = gone?.SourceUrl,
                            RecipeRemovedNote = gone == null ? null : RecipeRemovalService.RemovedNote,
                            RecipeImage = e.Recipe?.Image,
                            IngredientId = e.IngredientId,
                            IngredientName = e.Ingredient?.Name,
                            Quantity = e.Quantity,
                            MeasurementId = e.MeasurementId,
                            MeasurementName = e.Measurement?.Name,
                            FoodGroupId = e.Ingredient?.FoodGroupId,
                            FoodGroupName = e.Ingredient?.FoodGroup?.Name,
                            IsPrepared = isPrepared,
                            ServingsToBuy = isPrepared ? (e.Quantity ?? 1m) * Eaters(date, mt.Id) : null,
                            Title = gone?.Title ?? ProseWithholding.EntryTitle(e.Title, e.Recipe),
                            Notes = e.Note,
                            CompletedDate = e.CompletedDate,
                            ShoppingCompletedAt = e.ShoppingCompletedAt,
                        };

                        if (e.Recipe?.Nutrition != null)
                        {
                            entryModel.Calories = FindNutrientAmount(e.Recipe.Nutrition, CalorieNames);
                            entryModel.ProteinGrams = FindNutrientAmount(e.Recipe.Nutrition, ProteinNames);
                            entryModel.CarbGrams = FindNutrientAmount(e.Recipe.Nutrition, CarbNames);
                            entryModel.FatGrams = FindNutrientAmount(e.Recipe.Nutrition, FatNames);
                        }
                        else if (e.Ingredient?.IngredientNutrients is { Count: > 0 })
                        {
                            // Standalone whole food. Stored amounts are per 100 g; one unit of
                            // Quantity means one reference serving (the source's standard portion),
                            // falling back to the 100 g basis when the food has no reference portion.
                            // Per-person scaling from caloric need happens downstream (portions).
                            // Guard the > 0 case: a zero/negative reference would zero out nutrition.
                            var basisGrams = e.Ingredient.ReferenceServingGrams is > 0
                                ? e.Ingredient.ReferenceServingGrams.Value
                                : 100m;
                            var factor = (basisGrams / 100m) * (e.Quantity ?? 1m);
                            entryModel.Calories = FindIngredientNutrient(e.Ingredient.IngredientNutrients, CalorieNames) * factor;
                            entryModel.ProteinGrams = FindIngredientNutrient(e.Ingredient.IngredientNutrients, ProteinNames) * factor;
                            entryModel.CarbGrams = FindIngredientNutrient(e.Ingredient.IngredientNutrients, CarbNames) * factor;
                            entryModel.FatGrams = FindIngredientNutrient(e.Ingredient.IngredientNutrients, FatNames) * factor;
                            entryModel.ReferenceServingGrams = e.Ingredient.ReferenceServingGrams;
                        }

                        return entryModel;
                    }).ToList();

                    var hasNutrition = entryModels.Any(e => e.Calories.HasValue);

                    return new MealPlanCellModel
                    {
                        MealTypeId = mt.Id,
                        MealType = slotEntries.FirstOrDefault()?.MealType?.Name ?? mt.Name,
                        Entries = entryModels,
                        TotalCalories = hasNutrition ? entryModels.Sum(e => e.Calories ?? 0) : null,
                        TotalProteinGrams = hasNutrition ? entryModels.Sum(e => e.ProteinGrams ?? 0) : null,
                        TotalCarbGrams = hasNutrition ? entryModels.Sum(e => e.CarbGrams ?? 0) : null,
                        TotalFatGrams = hasNutrition ? entryModels.Sum(e => e.FatGrams ?? 0) : null,
                    };
                }).ToList();

                days.Add(new MealPlanDayModel
                {
                    Date = date,
                    DayOfWeek = date.DayOfWeek.ToString(),
                    Cells = cells,
                    Exclusions = dayExclusions.Select(e => new MealPlanExclusionResponseModel
                    {
                        Id = e.Id,
                        HouseholdId = e.HouseholdId,
                        PersonId = e.PersonId,
                        PersonName = e.Person?.Name ?? string.Empty,
                        Date = e.Date,
                        MealTypeId = e.MealTypeId,
                        MealType = e.MealType?.Name
                    }).ToList()
                });
            }

            return new MealPlanWeekResponseModel
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                Days = days
            };
        }

        public async Task<MealPlanExclusionResponseModel> CreateExclusionAsync(MealPlanExclusionCreateModel model)
        {
            var exclusion = new MealPlanExclusionEntity
            {
                HouseholdId = model.HouseholdId,
                PersonId = model.PersonId,
                Date = model.Date,
                MealTypeId = model.MealTypeId,
                CreatedDate = DateTime.UtcNow
            };

            _context.MealPlanExclusions.Add(exclusion);
            await _context.SaveChangesAsync();

            await _context.Entry(exclusion).Reference(e => e.Person).LoadAsync();
            if (exclusion.MealTypeId.HasValue)
                await _context.Entry(exclusion).Reference(e => e.MealType).LoadAsync();

            return new MealPlanExclusionResponseModel
            {
                Id = exclusion.Id,
                HouseholdId = exclusion.HouseholdId,
                PersonId = exclusion.PersonId,
                PersonName = exclusion.Person?.Name ?? string.Empty,
                Date = exclusion.Date,
                MealTypeId = exclusion.MealTypeId,
                MealType = exclusion.MealType?.Name
            };
        }

        public async Task<MealPlanExclusionResponseModel?> GetExclusionAsync(long id)
        {
            var exclusion = await _context.MealPlanExclusions
                .Include(e => e.Person)
                .Include(e => e.MealType)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (exclusion == null) return null;

            return new MealPlanExclusionResponseModel
            {
                Id = exclusion.Id,
                HouseholdId = exclusion.HouseholdId,
                PersonId = exclusion.PersonId,
                PersonName = exclusion.Person?.Name ?? string.Empty,
                Date = exclusion.Date,
                MealTypeId = exclusion.MealTypeId,
                MealType = exclusion.MealType?.Name
            };
        }

        public async Task<List<MealPlanExclusionResponseModel>> GetExclusionsAsync(long householdId, DateOnly start, DateOnly end)
        {
            var exclusions = await _context.MealPlanExclusions
                .Include(e => e.Person)
                .Include(e => e.MealType)
                .Where(e => e.HouseholdId == householdId && e.Date >= start && e.Date <= end)
                .ToListAsync();

            return exclusions.Select(e => new MealPlanExclusionResponseModel
            {
                Id = e.Id,
                HouseholdId = e.HouseholdId,
                PersonId = e.PersonId,
                PersonName = e.Person?.Name ?? string.Empty,
                Date = e.Date,
                MealTypeId = e.MealTypeId,
                MealType = e.MealType?.Name
            }).ToList();
        }

        public async Task<bool> DeleteExclusionAsync(long id)
        {
            var exclusion = await _context.MealPlanExclusions.FindAsync(id);
            if (exclusion == null)
                return false;

            _context.MealPlanExclusions.Remove(exclusion);
            await _context.SaveChangesAsync();
            return true;
        }

        private static decimal? FindNutrientAmount(ICollection<RecipeNutritionEntity> nutrition, string[] namePatterns)
        {
            var match = nutrition.FirstOrDefault(n =>
                n.Nutrient != null && n.Nutrient.ParentNutrientId == null && namePatterns.Any(p =>
                    n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)));
            return match?.Amount;
        }

        private static decimal? FindIngredientNutrient(
            ICollection<Nom.Data.Nutrient.IngredientNutrientEntity> nutrients, string[] namePatterns)
        {
            var match = nutrients.FirstOrDefault(n =>
                n.Nutrient != null && n.Nutrient.ParentNutrientId == null && namePatterns.Any(p =>
                    n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)));
            return match?.Amount;
        }
    }
}
