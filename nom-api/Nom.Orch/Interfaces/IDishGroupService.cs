using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Canonical dish groups ("chocolate chip cookies") — recipes that are
    /// takes on the same dish share a group. Groups are created lazily from
    /// suggested names, browsable by anyone, and correctable by curation
    /// admins (reassign / merge).
    /// </summary>
    public interface IDishGroupService
    {
        /// <summary>Finds or creates the group for a canonical name (case-insensitive, slug-keyed).</summary>
        Task<DishGroupModel> GetOrCreateAsync(string canonicalName);

        /// <summary>All groups with member counts, largest first.</summary>
        Task<List<DishGroupModel>> ListAsync(int limit = 200);

        /// <summary>Group + its recipes visible to the caller; null when the slug is unknown.</summary>
        Task<DishGroupDetailModel?> GetBySlugAsync(string slug, long? viewerPersonId);

        /// <summary>Assigns a recipe to a group (or clears with null). Returns false when the recipe is unknown.</summary>
        Task<bool> AssignAsync(long recipeId, long? dishGroupId);

        /// <summary>Moves every recipe from one group into another and soft-deletes the source.</summary>
        Task<bool> MergeAsync(long sourceGroupId, long targetGroupId, long adminPersonId);
    }
}
