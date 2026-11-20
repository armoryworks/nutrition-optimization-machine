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
        /// Classifies unclassified ingredients into food groups from their stored USDA category only
        /// (never from a name); an ingredient without a stored category is left alone. When
        /// <paramref name="overwrite"/> is true, sets already-classified ingredients to what their
        /// category gives too, clearing the group where it gives none. Every change is journaled as
        /// GroupFromCategory under one stamp, by <paramref name="personId"/> (the system when none),
        /// so `Nom.Import --admit --undo &lt;stamp&gt;` takes the run back. Returns the number of
        /// ingredients updated.
        /// </summary>
        Task<int> AutoClassifyFoodGroupsAsync(bool overwrite, long? personId = null);
    }
}