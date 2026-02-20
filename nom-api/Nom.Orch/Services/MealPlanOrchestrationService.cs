// File: Nom.Orch/Services/MealPlan/MealPlanOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;

namespace Nom.Orch.Services
{
    public class MealPlanOrchestrationService : IMealPlanOrchestrationService
    {
        private readonly ApplicationDbContext _context;

        public MealPlanOrchestrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<MealPlanResponseModel>> GetAllMealPlansAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.MealPlans
                .Include(mp => mp.Recipe)
                .Include(mp => mp.MealType)
                .AsQueryable();

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
            var rule = await _context.MealPlanRules.FindAsync(id);
            if (rule == null)
                return null;

            return new MealPlanRuleResponseModel
            {
                Id = rule.Id,
                HouseholdId = rule.HouseholdId,
                DayOfWeekId = rule.DayOfWeekId,
                DayOfWeek = "Monday", // Placeholder - would get from reference
                MealTypeId = rule.MealTypeId,
                MealType = "Meal", // Placeholder - would get from reference
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
