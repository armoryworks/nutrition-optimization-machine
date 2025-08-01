// File: Nom.Orch/Interfaces/IRecipeOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    public interface IRecipeOrchestrationService
    {
        Task<List<RecipeResponseModel>> GetAllRecipesAsync();
        Task<RecipeCreateResponseModel> CreateRecipeAsync(RecipeCreateModel model);
        Task<RecipeResponseModel?> GetRecipeAsync(long id);
        Task<RecipeResponseModel?> UpdateRecipeAsync(long id, RecipeUpdateModel model);
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
    }
}