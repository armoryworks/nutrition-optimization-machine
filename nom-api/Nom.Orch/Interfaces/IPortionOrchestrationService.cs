using Nom.Orch.Models.MealPlan;
using Nom.Orch.Models.Plan;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Meal-split settings and per-member portion computation: how a household's
    /// calorie budget maps onto planned meals as cook factors and plate counts.
    /// </summary>
    public interface IPortionOrchestrationService
    {
        Task<MealSplitModel> GetMealSplitAsync(long householdId);
        Task<MealSplitModel> SaveMealSplitAsync(long householdId, MealSplitModel model);

        /// <summary>Portion breakdown for one planned meal cell (household + date + meal type).</summary>
        Task<PortionBreakdownModel?> ComputePortionsAsync(long householdId, DateOnly date, long mealTypeId);

        /// <summary>
        /// Rescales each recipe cell's nutrition totals in <paramref name="week"/> to the person's
        /// calorie-scaled portion (one plate is one serving of each recipe in the cell).
        /// </summary>
        Task ScaleWeekToPersonAsync(long householdId, long personId, MealPlanWeekResponseModel week);

        /// <summary>Cook factors for every planned recipe in a date range (shopping-list scaling).</summary>
        Task<List<RangeCookFactorModel>> ComputeRangeCookFactorsAsync(long householdId, DateOnly startDate, DateOnly endDate);
    }
}
