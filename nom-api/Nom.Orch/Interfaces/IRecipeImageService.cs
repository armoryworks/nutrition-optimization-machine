using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    public enum RecipeImageReviewResult
    {
        Ok,
        NotFound,
        NotPending,
        RecipeHasImage,
    }

    /// <summary>
    /// Openly licensed photos for published recipes, proposed by the external image finder.
    /// nom-api never fetches the photos itself: rehosted bytes arrive with the submission and
    /// hotlink-only sources are stored as their remote URL. See docs/architecture/image-sources.md.
    /// </summary>
    public interface IRecipeImageService
    {
        /// <summary>Published recipes with no image and nothing waiting in the queue, most-rated first.</summary>
        Task<List<RecipeImageWorkItemModel>> GetMissingAsync(int limit);

        /// <summary>Stores the finder's candidates; "attached" ones are applied to the recipe at once.</summary>
        Task<RecipeImageSubmissionResultModel?> SubmitAsync(RecipeImageSubmissionModel submission, long? personId);

        Task<List<RecipeImageCandidateModel>> GetCandidatesAsync(string status, int limit);

        /// <summary>The rehosted bytes of a candidate, for the review screen. Null for hotlinked or missing ones.</summary>
        Task<(byte[] Data, string ContentType)?> GetCandidateImageAsync(long candidateId);

        /// <summary>Attaches a pending candidate to its recipe and supersedes its siblings.</summary>
        Task<RecipeImageReviewResult> ApproveAsync(long candidateId, long? personId);

        Task<RecipeImageReviewResult> RejectAsync(long candidateId, long? personId);

        /// <summary>Takes an attached or approved photo back off its recipe (e.g. a licence takedown).</summary>
        Task<RecipeImageReviewResult> RemoveAsync(long candidateId, long? personId);

        /// <summary>In-use Unsplash photos whose download event the finder still has to report.</summary>
        Task<List<RecipeImageDownloadTrackingModel>> GetPendingDownloadTrackingAsync();

        Task<bool> MarkDownloadTrackedAsync(long candidateId);
    }
}
