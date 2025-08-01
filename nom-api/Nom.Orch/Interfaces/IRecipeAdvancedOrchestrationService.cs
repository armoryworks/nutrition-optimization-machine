using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    public interface IRecipeAdvancedOrchestrationService
    {
        // Comments
        Task<RecipeCommentResponseModel> CreateCommentAsync(RecipeCommentCreateModel model);
        Task<List<RecipeCommentResponseModel>> GetRecipeCommentsAsync(long recipeId);
        Task<bool> DeleteCommentAsync(long commentId);

        // Ratings
        Task<RecipeRatingResponseModel> CreateRatingAsync(RecipeRatingCreateModel model);
        Task<RecipeRatingResponseModel?> GetUserRatingAsync(long recipeId);
        Task<decimal> GetRecipeAverageRatingAsync(long recipeId);
        Task<bool> UpdateRatingAsync(long ratingId, RecipeRatingCreateModel model);
        Task<bool> DeleteRatingAsync(long ratingId);

        // Share Tokens
        Task<RecipeShareTokenResponseModel> CreateShareTokenAsync(RecipeShareTokenCreateModel model);
        Task<List<RecipeShareTokenResponseModel>> GetRecipeShareTokensAsync(long recipeId);
        Task<bool> DeleteShareTokenAsync(long shareTokenId);
        Task<RecipeShareTokenResponseModel?> GetRecipeByShareTokenAsync(string shareToken);

        // Timeline Events
        Task<RecipeTimelineEventResponseModel> CreateTimelineEventAsync(RecipeTimelineEventCreateModel model);
        Task<List<RecipeTimelineEventResponseModel>> GetRecipeTimelineEventsAsync(long recipeId);
        Task<bool> DeleteTimelineEventAsync(long eventId);

        // Notes
        Task<RecipeNoteResponseModel> CreateNoteAsync(RecipeNoteCreateModel model);
        Task<List<RecipeNoteResponseModel>> GetRecipeNotesAsync(long recipeId);
        Task<bool> UpdateNoteAsync(long noteId, RecipeNoteCreateModel model);
        Task<bool> DeleteNoteAsync(long noteId);

        // Recipe Actions (Last Made, etc.)
        Task<bool> MarkRecipeAsMadeAsync(long recipeId);
        Task<DateTime?> GetRecipeLastMadeAsync(long recipeId);
    }
} 