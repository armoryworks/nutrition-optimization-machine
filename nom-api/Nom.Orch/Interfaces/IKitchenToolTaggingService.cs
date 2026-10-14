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

        /// <summary>Adds the tool to the catalog, or makes the suggestion and its grouped variants aliases of an existing tool, and links it to every recipe it was seen in. Null when not found or not pending.</summary>
        Task<KitchenToolSuggestionApproveResultModel?> ApproveAsync(long suggestionId, KitchenToolSuggestionApproveModel request, long? personId);

        /// <summary>Rejects the suggestion and every variant grouped under it.</summary>
        Task<bool> RejectAsync(long suggestionId);

        /// <summary>Groups a pending suggestion (and its variants) under another pending one, which takes over its recipes.</summary>
        Task<bool> MergeSuggestionAsync(long suggestionId, long intoSuggestionId);

        /// <summary>Retires an approved tool, moving its recipe links, household answers and aliases to another tool. Built-in tools cannot be merged away. Null when either tool is unknown.</summary>
        Task<KitchenToolMergeResultModel?> MergeToolAsync(long toolId, long intoToolId);

        /// <summary>Renames an approved tool. Built-in tools keep their names.</summary>
        Task<bool> RenameToolAsync(long toolId, string name);

        /// <summary>Every tool a suggestion can be aliased to: the built-in catalog plus approved tools.</summary>
        Task<List<KitchenToolOptionModel>> GetToolsAsync();
    }
}
