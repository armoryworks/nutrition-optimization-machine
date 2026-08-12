using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Recipe-scoped substitutions (with per-step effects) and augmentation
    /// add-ins. Regular users see curated items only; curators also see
    /// machine-proposed (non-curated) entries so they can review them.
    /// </summary>
    public interface IRecipeEnhancementService
    {
        Task<List<RecipeSubstitutionModel>> GetSubstitutionsAsync(long recipeId, bool includeUncurated);

        Task<List<RecipeAugmentationModel>> GetAugmentationsAsync(long recipeId, bool includeUncurated);

        /// <summary>Curator-created entries are curated immediately.</summary>
        Task<RecipeSubstitutionModel> UpsertSubstitutionAsync(long recipeId, long? substitutionId, RecipeSubstitutionUpsertModel model, long personId);

        Task<RecipeAugmentationModel> UpsertAugmentationAsync(long recipeId, long? augmentationId, RecipeAugmentationUpsertModel model, long personId);

        Task<bool> DeleteSubstitutionAsync(long recipeId, long substitutionId, long personId);

        Task<bool> DeleteAugmentationAsync(long recipeId, long augmentationId, long personId);

        /// <summary>Marks a machine-proposed entry as curated so it appears to users.</summary>
        Task<bool> CurateSubstitutionAsync(long recipeId, long substitutionId, long personId);

        Task<bool> CurateAugmentationAsync(long recipeId, long augmentationId, long personId);
    }
}
