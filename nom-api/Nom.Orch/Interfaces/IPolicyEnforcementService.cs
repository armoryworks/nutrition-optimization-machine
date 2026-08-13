using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Reads member policies (household-policies design doc §3) for enforcement:
    /// feature gates, curated-only mode, and locked-restriction compliance.
    /// </summary>
    public interface IPolicyEnforcementService
    {
        /// <summary>True when the member's policy explicitly gates the feature (absent key/row = allowed).</summary>
        Task<bool> IsFeatureGatedAsync(long personId, long householdId, string gateKey);

        /// <summary>
        /// True when the person's policy gates the feature in ANY household
        /// they are an active member of (used for person-scoped actions like
        /// creating or importing recipes, which have no single household context).
        /// </summary>
        Task<bool> IsFeatureGatedAnywhereAsync(long personId, string gateKey);

        /// <summary>True when the member is in curated-only mode for the household.</summary>
        Task<bool> IsCuratedOnlyAsync(long personId, long householdId);

        /// <summary>
        /// Ingredient ids that appear in LOCKED restrictions of active members
        /// of the household — recipes containing them are hard-blocked.
        /// </summary>
        Task<List<long>> GetLockedIngredientIdsAsync(long householdId);
    }
}
