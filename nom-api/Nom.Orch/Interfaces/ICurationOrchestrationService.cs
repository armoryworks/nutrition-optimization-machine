using Nom.Orch.Models.Curation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface ICurationOrchestrationService
    {
        Task SubmitForCurationAsync(SubmitForCurationRequest request, long authorId);
        Task ApproveAsync(CurationDecisionRequest request, long adminId);
        Task RequestRevisionAsync(CurationDecisionRequest request, long adminId);
        Task RejectAsync(CurationDecisionRequest request, long adminId);
        Task<List<CurationQueueItemModel>> GetCurationQueueAsync();

        /// <summary>Sets or clears (null) an ingredient's nutritional food group. Returns true if the ingredient exists.</summary>
        Task<bool> SetIngredientFoodGroupAsync(long ingredientId, long? foodGroupId);

        /// <summary>
        /// Heuristically classifies unclassified ingredients into food groups by name keywords.
        /// When <paramref name="overwrite"/> is true, re-classifies already-classified ingredients too.
        /// Returns the number of ingredients updated.
        /// </summary>
        Task<int> AutoClassifyFoodGroupsAsync(bool overwrite);
    }
}