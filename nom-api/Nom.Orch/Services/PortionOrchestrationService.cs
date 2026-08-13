using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;

namespace Nom.Orch.Services
{
    public class PortionOrchestrationService : IPortionOrchestrationService
    {
        private const string MealSplitPreferenceKey = "meal-split";
        private const string CaloriesNutrientName = "Calories";
        private const decimal DefaultDailyCalories = 2000m;

        // Mirrors MealPlanOrchestrationService.MealTypes (private there).
        private static readonly Dictionary<long, string> MealTypeNames = new()
        {
            [1100L] = "Breakfast",
            [1101L] = "Lunch",
            [1102L] = "Dinner",
            [1103L] = "Snacks",
        };

        private readonly ApplicationDbContext _context;
        private readonly ILogger<PortionOrchestrationService> _logger;

        public PortionOrchestrationService(
            ApplicationDbContext context,
            ILogger<PortionOrchestrationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<MealSplitModel> GetMealSplitAsync(long householdId)
        {
            var pref = await _context.HouseholdPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.HouseholdId == householdId
                    && p.PreferenceKey == MealSplitPreferenceKey);

            if (pref?.PreferenceValue == null) return new MealSplitModel();

            try
            {
                return JsonSerializer.Deserialize<MealSplitModel>(pref.PreferenceValue,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new MealSplitModel();
            }
            catch (JsonException)
            {
                _logger.LogWarning("Malformed meal-split preference for household {HouseholdId}; using defaults", householdId);
                return new MealSplitModel();
            }
        }

        public async Task<MealSplitModel> SaveMealSplitAsync(long householdId, MealSplitModel model)
        {
            if (Math.Abs(model.Total - 100m) > 0.01m)
            {
                throw new ArgumentException($"Meal split percentages must sum to 100 (got {model.Total}).");
            }

            var pref = await _context.HouseholdPreferences
                .FirstOrDefaultAsync(p => p.HouseholdId == householdId
                    && p.PreferenceKey == MealSplitPreferenceKey);

            if (pref == null)
            {
                pref = new HouseholdPreferenceEntity
                {
                    HouseholdId = householdId,
                    PreferenceKey = MealSplitPreferenceKey,
                    DataType = "json",
                };
                _context.HouseholdPreferences.Add(pref);
            }

            pref.PreferenceValue = JsonSerializer.Serialize(model);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved meal split for household {HouseholdId}", householdId);
            return model;
        }

        public async Task<PortionBreakdownModel?> ComputePortionsAsync(long householdId, DateOnly date, long mealTypeId)
        {
            var entries = await _context.MealPlans
                .AsNoTracking()
                .Where(mp => mp.HouseholdId == householdId
                    && mp.Date == date
                    && mp.MealTypeId == mealTypeId
                    && mp.RecipeId != null)
                .Include(mp => mp.Recipe)
                .ToListAsync();

            if (entries.Count == 0) return null;

            var members = await LoadMemberTargetsAsync(householdId);
            if (members.Count == 0) return null;

            var recipeIds = entries.Select(e => e.RecipeId!.Value).Distinct().ToList();
            var perServingCalories = await LoadPerServingCaloriesAsync(recipeIds);

            var split = await GetMealSplitAsync(householdId);
            var dailyTotal = members.Sum(m => m.TargetCalories);
            var budget = dailyTotal * MealPct(split, mealTypeId) / 100m;

            var result = new PortionBreakdownModel
            {
                MealTypeId = mealTypeId,
                MealType = MealTypeNames.GetValueOrDefault(mealTypeId, "Meal"),
                BudgetCalories = Math.Round(budget, 0),
            };

            var plateCalories = recipeIds.Sum(id => perServingCalories.GetValueOrDefault(id, 0m));
            result.PlateCalories = Math.Round(plateCalories, 0);

            if (plateCalories <= 0m)
            {
                result.NoNutritionData = true;
                result.Members = members;
                return result;
            }

            foreach (var member in members)
            {
                member.SharePct = Math.Round(member.TargetCalories / dailyTotal * 100m, 1);
                member.Plates = Math.Round(member.TargetCalories / dailyTotal * budget / plateCalories, 2);
                member.Calories = Math.Round(member.Plates * plateCalories, 0);
            }
            result.Members = members;
            result.TotalPlates = Math.Round(members.Sum(m => m.Plates), 2);

            foreach (var entry in entries.DistinctBy(e => e.RecipeId))
            {
                var recipeId = entry.RecipeId!.Value;
                var servings = entry.Recipe?.RecipeServings
                    ?? (decimal?)entry.Recipe?.Servings
                    ?? 0m;
                if (servings <= 0m) servings = 1m;

                result.Recipes.Add(new PortionRecipeModel
                {
                    RecipeId = recipeId,
                    Name = entry.Recipe?.Name ?? string.Empty,
                    PerServingCalories = Math.Round(perServingCalories.GetValueOrDefault(recipeId, 0m), 0),
                    RecipeServings = servings,
                    CookFactor = Math.Round(result.TotalPlates / servings, 2),
                });
            }

            return result;
        }

        public async Task<List<RangeCookFactorModel>> ComputeRangeCookFactorsAsync(long householdId, DateOnly startDate, DateOnly endDate)
        {
            var results = new List<RangeCookFactorModel>();

            var cells = await _context.MealPlans
                .AsNoTracking()
                .Where(mp => mp.HouseholdId == householdId
                    && mp.Date >= startDate && mp.Date <= endDate
                    && mp.RecipeId != null)
                .Select(mp => new { mp.Date, mp.MealTypeId })
                .Distinct()
                .ToListAsync();

            foreach (var cell in cells)
            {
                var breakdown = await ComputePortionsAsync(householdId, cell.Date, cell.MealTypeId);
                if (breakdown == null || breakdown.NoNutritionData) continue;

                results.AddRange(breakdown.Recipes.Select(r => new RangeCookFactorModel
                {
                    Date = cell.Date,
                    MealTypeId = cell.MealTypeId,
                    RecipeId = r.RecipeId,
                    CookFactor = r.CookFactor,
                }));
            }

            return results;
        }

        private static decimal MealPct(MealSplitModel split, long mealTypeId) => mealTypeId switch
        {
            1100L => split.BreakfastPct,
            1101L => split.LunchPct,
            1102L => split.DinnerPct,
            1103L => split.SnacksPct,
            _ => 25m,
        };

        /// <summary>Active members with resolved daily calorie targets (person goal → household goal → default).</summary>
        private async Task<List<PortionMemberModel>> LoadMemberTargetsAsync(long householdId)
        {
            var members = await _context.HouseholdMembers
                .AsNoTracking()
                .Where(hm => hm.HouseholdId == householdId && hm.IsActive)
                .Select(hm => new { hm.PersonId, hm.Person.Name })
                .ToListAsync();

            var personIds = members.Select(m => m.PersonId).ToList();
            var personGoals = await _context.MacroGoals
                .AsNoTracking()
                .Where(g => g.PersonId != null && personIds.Contains(g.PersonId.Value) && g.CaloriesTarget != null)
                .ToDictionaryAsync(g => g.PersonId!.Value, g => g.CaloriesTarget!.Value);

            var householdDefault = await _context.MacroGoals
                .AsNoTracking()
                .Where(g => g.HouseholdId == householdId)
                .Select(g => g.CaloriesTarget)
                .FirstOrDefaultAsync();

            return members.Select(m =>
            {
                var (target, source) = personGoals.TryGetValue(m.PersonId, out var own)
                    ? (own, "person")
                    : householdDefault.HasValue
                        ? (householdDefault.Value, "household")
                        : (DefaultDailyCalories, "default");
                return new PortionMemberModel
                {
                    PersonId = m.PersonId,
                    Name = m.Name,
                    TargetCalories = target,
                    TargetSource = source,
                };
            }).ToList();
        }

        private async Task<Dictionary<long, decimal>> LoadPerServingCaloriesAsync(List<long> recipeIds)
        {
            return await _context.RecipeNutrition
                .AsNoTracking()
                .Where(rn => recipeIds.Contains(rn.RecipeId) && rn.Nutrient!.Name == CaloriesNutrientName)
                .GroupBy(rn => rn.RecipeId)
                .Select(g => new { RecipeId = g.Key, Amount = g.Sum(rn => rn.Amount) })
                .ToDictionaryAsync(x => x.RecipeId, x => x.Amount);
        }
    }
}
