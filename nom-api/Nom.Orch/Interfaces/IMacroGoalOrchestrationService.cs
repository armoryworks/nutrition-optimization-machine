using Nom.Orch.Models.Plan;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Manages daily macronutrient goals for persons and households, and
    /// resolves the effective goals that apply to a person (own goal first,
    /// household default second).
    /// </summary>
    public interface IMacroGoalOrchestrationService
    {
        Task<MacroGoalModel?> GetPersonGoalAsync(long personId);
        Task<MacroGoalModel> SavePersonGoalAsync(long personId, MacroGoalModel model);
        Task<MacroGoalModel?> GetHouseholdGoalAsync(long householdId);
        Task<MacroGoalModel> SaveHouseholdGoalAsync(long householdId, MacroGoalModel model);

        /// <summary>Person's own goal, else their active household's goal, else Source="none".</summary>
        Task<EffectiveMacroGoalModel> GetEffectiveForPersonAsync(long personId);

        /// <summary>Household goal targets keyed for shuffle scoring; null when the household has no goal.</summary>
        Task<MacroGoalModel?> GetEffectiveForHouseholdAsync(long householdId);
    }
}
