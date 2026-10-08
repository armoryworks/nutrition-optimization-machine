using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Household;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// A household's kitchen: which tools it has, what to do with recipes that need a tool it
    /// lacks (hide, warn or ignore), and per-recipe checks against that kitchen.
    /// </summary>
    public interface IKitchenToolService
    {
        Task<KitchenSettingsModel> GetSettingsAsync(long householdId);
        Task<KitchenSettingsModel> UpdateSettingsAsync(long householdId, KitchenSettingsUpdateModel update);

        /// <summary>The household's mode, defaulting to warn.</summary>
        Task<string> GetModeAsync(long householdId);

        /// <summary>Tool ids the household has, with catalog defaults for tools it never answered.</summary>
        Task<IReadOnlySet<long>> GetOwnedToolIdsAsync(long householdId);

        /// <summary>Null when the recipe doesn't exist.</summary>
        Task<RecipeToolCheckModel?> CheckRecipeAsync(long householdId, long recipeId);
    }
}
