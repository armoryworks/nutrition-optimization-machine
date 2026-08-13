// File: Nom.Orch/Services/MealPlan/MealPlanOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;

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

            return mealPlans.Select(mp => new MealPlanResponseModel
            {
                Id = mp.Id,
                HouseholdId = mp.HouseholdId,
                AuthorId = mp.AuthorId,
                Date = mp.Date,
                MealTypeId = mp.MealTypeId,
                MealType = mp.MealType?.Name ?? "Meal",
                Title = mp.Title,
                Notes = mp.Note,
                RecipeId = mp.RecipeId,
                RecipeName = mp.Recipe?.Name,
                CreatedDate = mp.CreatedDate,
                ModifiedDate = mp.LastModifiedDate
            }).ToList();
        }

        public async Task<MealPlanCreateResponseModel> CreateMealPlanAsync(MealPlanCreateModel model, long authorId)
        {
            await ValidateSlotAssignmentAsync(model.HouseholdId, authorId, model.RecipeId);

            var mealPlan = new MealPlanEntity
            {
                HouseholdId = model.HouseholdId,
                AuthorId = authorId,
                Date = model.Date,
                MealTypeId = model.MealTypeId,
                Title = model.Title,
                Note = model.Notes,
                RecipeId = model.RecipeId,
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
                Title = mealPlan.Title,
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

            return new MealPlanResponseModel
            {
                Id = mealPlan.Id,
                HouseholdId = mealPlan.HouseholdId,
                AuthorId = mealPlan.AuthorId,
                Date = mealPlan.Date,
                MealTypeId = mealPlan.MealTypeId,
                MealType = mealPlan.MealType?.Name ?? "Meal",
                Title = mealPlan.Title,
                Notes = mealPlan.Note,
                RecipeId = mealPlan.RecipeId,
                RecipeName = mealPlan.Recipe?.Name,
                CreatedDate = mealPlan.CreatedDate,
                ModifiedDate = mealPlan.LastModifiedDate
            };
        }

        public async Task<MealPlanResponseModel?> UpdateMealPlanAsync(long id, MealPlanUpdateModel model)
        {
            var mealPlan = await _context.MealPlans
                .Include(mp => mp.Recipe)
                .Include(mp => mp.MealType)
                .FirstOrDefaultAsync(mp => mp.Id == id);
            if (mealPlan == null)
                return null;

            await ValidateSlotAssignmentAsync(mealPlan.HouseholdId, mealPlan.AuthorId, model.RecipeId);

            mealPlan.Date = model.Date;
            mealPlan.MealTypeId = model.MealTypeId;
            mealPlan.Title = model.Title;
            mealPlan.Note = model.Notes;
            mealPlan.RecipeId = model.RecipeId;
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
                Title = mealPlan.Title,
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

        public async Task<MealPlanRuleCreateResponseModel> CreateRuleAsync(MealPlanRuleCreateModel model)
        {
            var rule = new MealPlanRuleEntity
            {
                HouseholdId = model.HouseholdId,
                DayOfWeekId = model.DayOfWeekId,
                MealTypeId = model.MealTypeId,
                QueryFilter = model.QueryFilterString,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.MealPlanRules.Add(rule);
            await _context.SaveChangesAsync();

            return new MealPlanRuleCreateResponseModel
            {
                Id = rule.Id,
                HouseholdId = rule.HouseholdId,
                DayOfWeekId = rule.DayOfWeekId,
                MealTypeId = rule.MealTypeId,
                QueryFilterString = rule.QueryFilter ?? string.Empty,
                CreatedDate = rule.CreatedDate
            };
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
                DayOfWeek = rule.DayOfWeek?.Name ?? string.Empty,
                MealTypeId = rule.MealTypeId,
                MealType = rule.MealType?.Name ?? string.Empty,
                QueryFilterString = rule.QueryFilter ?? string.Empty,
                CreatedDate = rule.CreatedDate,
                ModifiedDate = rule.LastModifiedDate
            };
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

        // Meal composition templates: defines how many recipes of each type compose a meal
        private static readonly Dictionary<long, (long RecipeTypeId, string Label)[]> MealComposition = new()
        {
            [1100] = new[] { (3101L, "Entree"), (3103L, "Fruit/Vegetable") },       // Breakfast: 2
            [1101] = new[] { (3100L, "Appetizer"), (3101L, "Entree"), (3102L, "Starch") }, // Lunch: 3
            [1102] = new[] { (3100L, "Appetizer"), (3101L, "Entree"), (3102L, "Starch") }, // Dinner: 3
            [1103] = new[] { (3104L, "Snack") },                                    // Snacks: 1
        };

        /// <summary>
        /// Slot assignments must reference a recipe the author can SEE and that
        /// does not violate any LOCKED restriction of an active household
        /// member (design doc §2: plan-slot assignment of non-compliant
        /// recipes is rejected; hard-block is reserved for locks).
        /// Previously these writes had no validation at all.
        /// </summary>
        private async Task ValidateSlotAssignmentAsync(long householdId, long authorId, long? recipeId)
        {
            var isMember = await _context.HouseholdMembers
                .AnyAsync(hm => hm.HouseholdId == householdId && hm.PersonId == authorId && hm.IsActive);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not an active member of this household.");
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

            // 2. Get restricted ingredient IDs for this household
            var restrictedIngredientIds = await _context.HouseholdMembers
                .Where(hm => hm.HouseholdId == model.HouseholdId && hm.IsActive)
                .SelectMany(hm => hm.Person.Restrictions)
                .Where(r => r.IngredientId.HasValue
                    && (r.EndDate == null || r.EndDate >= DateOnly.FromDateTime(DateTime.UtcNow))
                    && (r.BeginDate == null || r.BeginDate <= DateOnly.FromDateTime(DateTime.UtcNow)))
                .Select(r => r.IngredientId!.Value)
                .Distinct()
                .ToListAsync();

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
                foreach (var mt in MealTypes)
                {
                    if (!filledCells.Contains((date, mt.Id)))
                    {
                        emptyCells.Add((date, mt.Id));
                    }
                }
            }

            // 4. Expand each empty cell into composition sub-slots
            var expanded = new List<(DateOnly Date, long MealTypeId, long RecipeTypeId)>();
            foreach (var cell in emptyCells)
            {
                if (MealComposition.TryGetValue(cell.MealTypeId, out var slots))
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
                .GroupBy(e => (e.RecipeTypeId, e.MealTypeId))
                .ToDictionary(g => g.Key, g => g.Count());

            var recipePools = new Dictionary<(long RecipeTypeId, long MealTypeId), List<RecipeEntity>>();
            foreach (var ((recipeTypeId, mealTypeId), count) in countByTypeAndMeal)
            {
                // Pool = recipes the shuffling member can SEE (public pool +
                // household + audience tiers). curated_only members draw only
                // from audience-scoped recipes and household cookbooks.
                var query = _context.Recipes.VisibleTo(_context, authorId);
                if (await _policy.IsCuratedOnlyAsync(authorId, model.HouseholdId))
                {
                    query = query.Where(r =>
                        r.Visibility == RecipeVisibilityEnum.Audience
                        || _context.Set<Nom.Data.Plan.HouseholdCookbookRecipeEntity>().Any(cr =>
                            cr.RecipeId == r.Id && cr.HouseholdCookbook!.HouseholdId == model.HouseholdId));
                }

                if (restrictedIngredientIds.Count > 0)
                {
                    query = query.Where(r => !r.RecipeIngredients!
                        .Any(ri => restrictedIngredientIds.Contains(ri.IngredientId)));
                }

                if (recipeTypeId != 0)
                {
                    query = query.Where(r => r.RecipeTypes!.Any(rt => rt.Id == recipeTypeId));
                }

                // Prefer recipes whose category matches the meal type (e.g., breakfast entrees for breakfast)
                var mealAffineRecipes = await query
                    .Where(r => r.RecipeCategories!.Any(rc => rc.CategoryId == mealTypeId))
                    .OrderBy(r => EF.Functions.Random())
                    .Take(count)
                    .ToListAsync();

                // Fallback: if not enough meal-affine recipes, fill from the full pool
                if (mealAffineRecipes.Count < count)
                {
                    var existingIds = mealAffineRecipes.Select(r => r.Id).ToHashSet();
                    var fallback = await query
                        .Where(r => !existingIds.Contains(r.Id))
                        .OrderBy(r => EF.Functions.Random())
                        .Take(count - mealAffineRecipes.Count)
                        .ToListAsync();
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

            // 5b'. Household macro goals override the USDA defaults so shuffle
            // scoring steers toward the goals users actually set. A goal for a
            // macro with no guideline row (e.g. Calories has none in seed data)
            // is appended as a new scoring target rather than ignored.
            var macroGoal = await _context.MacroGoals
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.HouseholdId == model.HouseholdId);
            if (macroGoal != null)
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
            var trackedNutrientIds = guidelines.Select(g => g.NutrientId).ToList();
            var caloriesNutrientId = guidelines.FirstOrDefault(g => g.NutrientName == "Calories")?.NutrientId;

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
            var poolCounters = new Dictionary<(long, long), int>();
            var newEntities = new List<MealPlanEntity>();
            var dailyTotals = new Dictionary<DateOnly, Dictionary<long, decimal>>();
            var now = DateTime.UtcNow;

            foreach (var slot in expanded.OrderBy(s => s.Date).ThenBy(s => s.MealTypeId))
            {
                var poolKey = (slot.RecipeTypeId, slot.MealTypeId);
                if (!recipePools.TryGetValue(poolKey, out var pool) || pool.Count == 0)
                    continue;

                if (!dailyTotals.TryGetValue(slot.Date, out var dayTotals))
                {
                    dayTotals = new Dictionary<long, decimal>();
                    dailyTotals[slot.Date] = dayTotals;
                }

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

                    var isUnused = !usedIds.Contains(candidate.Id);
                    recipeNutrients.TryGetValue(candidate.Id, out var nutrients);

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
                    foreach (var (nutrientId, amount) in selectedNutrients)
                    {
                        dayTotals[nutrientId] = dayTotals.GetValueOrDefault(nutrientId, 0m) + amount;
                    }
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

            // 7. Bulk insert
            _context.MealPlans.AddRange(newEntities);
            await _context.SaveChangesAsync();

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
            };
        }

        // Meal type IDs from reference data seed
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
                .Where(mp => mp.HouseholdId == householdId && mp.Date >= weekStart && mp.Date <= weekEnd)
                .ToListAsync();

            var exclusions = await _context.MealPlanExclusions
                .Include(e => e.Person)
                .Include(e => e.MealType)
                .Where(e => e.HouseholdId == householdId && e.Date >= weekStart && e.Date <= weekEnd)
                .ToListAsync();

            var days = new List<MealPlanDayModel>();
            for (int i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                var dayMealPlans = mealPlans.Where(mp => mp.Date == date).ToList();
                var dayExclusions = exclusions.Where(e => e.Date == date).ToList();

                var cells = MealTypes.Select(mt =>
                {
                    var slotEntries = dayMealPlans.Where(mp => mp.MealTypeId == mt.Id).ToList();
                    var entryModels = slotEntries.Select(e =>
                    {
                        var entryModel = new MealPlanEntryModel
                        {
                            Id = e.Id,
                            RecipeId = e.RecipeId,
                            RecipeName = e.Recipe?.Name,
                            RecipeImage = e.Recipe?.Image,
                            Title = e.Title,
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
                n.Nutrient != null && namePatterns.Any(p =>
                    n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)));
            return match?.Amount;
        }
    }
}
