// File: Nom.Orch/Interfaces/IRecipeOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    public interface IRecipeOrchestrationService
    {
        Task<List<RecipeResponseModel>> GetAllRecipesAsync(long? currentPersonId = null);
        Task<List<RecipeResponseModel>> GetMyRecipesAsync(long personId);
        Task<RecipeCreateResponseModel> CreateRecipeAsync(RecipeCreateModel model, long currentPersonId);
        Task<RecipeResponseModel?> GetRecipeAsync(long id);
        Task<RecipeResponseModel?> UpdateRecipeAsync(long id, UpdateRecipeRequest model);
        Task<bool> DeleteRecipeAsync(long id);

        // Recipe Comments
        Task<RecipeCommentResponseModel> AddCommentAsync(RecipeCommentCreateModel model);
        Task<List<RecipeCommentResponseModel>> GetCommentsAsync(long recipeId);
        Task<bool> DeleteCommentAsync(long commentId);

        // Recipe Ratings
        Task<RecipeRatingResponseModel> AddRatingAsync(RecipeRatingCreateModel model);
        Task<List<RecipeRatingResponseModel>> GetRatingsAsync(long recipeId);
        Task<RecipeRatingResponseModel?> UpdateRatingAsync(long ratingId, RecipeRatingUpdateModel model);
        Task<bool> DeleteRatingAsync(long ratingId);

        // Recipe Ingredients
        Task<IngredientEditModel?> GetIngredientForEditAsync(long ingredientId);
        Task<IngredientEditModel> CreateIngredientAsync(CreateIngredientRequest model);
        Task<IngredientEditModel> UpdateIngredientAsync(UpdateIngredientRequest model);
        Task<List<IngredientEditModel>> GetMyIngredientsAsync(long personId);
        Task<List<IngredientSearchResponseModel>> SearchIngredientsAsync(string query);

        // Dashboard Analytics
        Task<RecipeDashboardAnalyticsModel> GetDashboardAnalyticsAsync(long personId);

        // Recipe Image/Assets
        Task<RecipeAssetResponseModel> UploadImageAsync(long recipeId, long personId, string fileName, string contentType, byte[] fileData);
        Task<(byte[] FileData, string ContentType)?> GetImageAsync(long recipeId, long? requestingPersonId);
        Task<bool> DeleteImageAsync(long recipeId, long assetId, long personId);
        Task<List<RecipeAssetResponseModel>> GetAssetsAsync(long recipeId);
    }
}