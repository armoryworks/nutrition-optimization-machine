using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The AI equipment lane: tags untagged recipes with kitchen tools, queues uncommon
    /// equipment it notices as suggestions, and turns approved suggestions into tools.
    /// </summary>
    public interface IKitchenToolTaggingService
    {
        /// <summary>Tags up to <paramref name="batchSize"/> untagged recipes. Returns null when the model is unavailable.</summary>
        Task<KitchenToolTaggingBatchResult?> TagNextBatchAsync(int batchSize, CancellationToken cancellationToken = default);

        Task<KitchenToolTaggingStatusModel> GetStatusAsync();
        Task<List<KitchenToolSuggestionModel>> GetSuggestionsAsync(string status);

        /// <summary>Adds the tool to the catalog and links it to every recipe it was seen in. Null when not found or not pending.</summary>
        Task<KitchenToolSuggestionApproveResultModel?> ApproveAsync(long suggestionId, KitchenToolSuggestionApproveModel request, long? personId);

        Task<bool> RejectAsync(long suggestionId);
    }
}
