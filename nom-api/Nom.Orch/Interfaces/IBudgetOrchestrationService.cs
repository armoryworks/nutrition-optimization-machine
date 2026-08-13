using Nom.Orch.Models.Plan;

namespace Nom.Orch.Interfaces
{
    /// <summary>Manages grocery-spend budgets and resolves the effective budget for a person.</summary>
    public interface IBudgetOrchestrationService
    {
        Task<BudgetModel?> GetPersonBudgetAsync(long personId);
        Task<BudgetModel> SavePersonBudgetAsync(long personId, BudgetModel model);
        Task<BudgetModel?> GetHouseholdBudgetAsync(long householdId);
        Task<BudgetModel> SaveHouseholdBudgetAsync(long householdId, BudgetModel model);
        Task<EffectiveBudgetModel> GetEffectiveForPersonAsync(long personId);
    }
}
