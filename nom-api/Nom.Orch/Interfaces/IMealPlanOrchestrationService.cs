// File: Nom.Orch/Interfaces/IMealPlanOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.MealPlan;

namespace Nom.Orch.Interfaces
{
    public interface IMealPlanOrchestrationService
    {
        Task<List<MealPlanResponseModel>> GetAllMealPlansAsync(DateTime? startDate = null, DateTime? endDate = null, List<long>? householdIds = null);
        Task<MealPlanCreateResponseModel> CreateMealPlanAsync(MealPlanCreateModel model, long authorId);
        Task<MealPlanResponseModel?> GetMealPlanAsync(long id);
        Task<MealPlanResponseModel?> UpdateMealPlanAsync(long id, MealPlanUpdateModel model);
        Task<bool> DeleteMealPlanAsync(long id);

        // Weekly calendar
        Task<MealPlanWeekResponseModel> GetWeekAsync(long householdId, DateOnly weekStart);

        // Meal Plan Rules
        Task<MealPlanRuleCreateResponseModel> CreateRuleAsync(MealPlanRuleCreateModel model);
        Task<MealPlanRuleResponseModel?> GetRuleAsync(long id);
        Task<bool> DeleteRuleAsync(long id);

        // Shuffle
        Task<MealPlanShuffleResponseModel> ShuffleMealPlansAsync(MealPlanShuffleModel model, long authorId);

        // Food-group requirements (household meal-composition rules)
        Task<List<FoodGroupModel>> GetFoodGroupsAsync();
        Task<List<FoodGroupRuleModel>> GetFoodGroupRulesAsync(long householdId);
        Task<FoodGroupRuleModel> UpsertFoodGroupRuleAsync(FoodGroupRuleUpsertModel model);
        Task<bool> DeleteFoodGroupRuleAsync(long id);

        // Shopping completion
        Task<bool> MarkShoppingCompletedAsync(long mealPlanId);

        // Meal Plan Exclusions
        Task<MealPlanExclusionResponseModel> CreateExclusionAsync(MealPlanExclusionCreateModel model);
        Task<MealPlanExclusionResponseModel?> GetExclusionAsync(long id);
        Task<List<MealPlanExclusionResponseModel>> GetExclusionsAsync(long householdId, DateOnly start, DateOnly end);
        Task<bool> DeleteExclusionAsync(long id);
    }
} 