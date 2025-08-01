// File: Nom.Orch/Interfaces/IMealPlanOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.MealPlan;

namespace Nom.Orch.Interfaces
{
    public interface IMealPlanOrchestrationService
    {
        Task<List<MealPlanResponseModel>> GetAllMealPlansAsync();
        Task<MealPlanCreateResponseModel> CreateMealPlanAsync(MealPlanCreateModel model);
        Task<MealPlanResponseModel?> GetMealPlanAsync(long id);
        Task<MealPlanResponseModel?> UpdateMealPlanAsync(long id, MealPlanUpdateModel model);
        Task<bool> DeleteMealPlanAsync(long id);

        // Meal Plan Rules
        Task<MealPlanRuleCreateResponseModel> CreateRuleAsync(MealPlanRuleCreateModel model);
        Task<MealPlanRuleResponseModel?> GetRuleAsync(long id);
        Task<bool> DeleteRuleAsync(long id);
    }
} 