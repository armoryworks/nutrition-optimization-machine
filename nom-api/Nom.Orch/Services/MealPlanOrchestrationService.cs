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
    }
} 