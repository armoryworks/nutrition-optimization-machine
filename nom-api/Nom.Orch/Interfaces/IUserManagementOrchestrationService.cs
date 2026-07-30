using Nom.Orch.Models.UserManagement;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IUserManagementOrchestrationService
    {
        Task UpdateUserClaimsAsync(UpdateUserClaimsRequest request);
        
        // User CRUD operations (from Mealie)
        Task<UserResponseModel?> GetCurrentUserAsync(string userId);
        Task<UserResponseModel> GetUserByIdAsync(string userId);
        Task<List<UserResponseModel>> GetAllUsersAsync();
        Task<UserResponseModel> CreateUserAsync(CreateUserRequestModel request);
        Task<UserResponseModel> UpdateUserAsync(string userId, UpdateUserRequestModel request);
        Task DeleteUserAsync(string userId);
        
        // User ratings and favorites (from Mealie)
        Task<List<UserRatingResponseModel>> GetUserRatingsAsync(string userId);
        Task<UserRatingResponseModel> GetUserRatingForRecipeAsync(string userId, long recipeId);
        Task<List<UserRatingResponseModel>> GetUserFavoritesAsync(string userId);
        
        // User authentication (from Mealie)
        Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequestModel request);
        Task<bool> ForgotPasswordAsync(ForgotPasswordRequestModel request);
        Task<bool> ResetPasswordAsync(ResetPasswordRequestModel request);
        
        // User registration (from Mealie)
        Task<UserResponseModel> RegisterUserAsync(RegisterUserRequestModel request);

        // User images and profile (from Mealie)
        Task<string> UploadUserImageAsync(string userId, byte[] imageData);
        Task<bool> DeleteUserImageAsync(string userId);
        
        // API tokens (from Mealie)
        Task<List<ApiTokenResponseModel>> GetUserApiTokensAsync(string userId);
        Task<ApiTokenResponseModel> CreateApiTokenAsync(string userId, CreateApiTokenRequestModel request);
        Task DeleteApiTokenAsync(string userId, string tokenId);
    }
}