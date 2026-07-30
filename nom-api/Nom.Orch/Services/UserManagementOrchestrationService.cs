using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;
using Nom.Orch.Models.Privacy;
using Nom.Orch.Models.UserManagement;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
namespace Nom.Orch.Services
{
    public class UserManagementOrchestrationService : IUserManagementOrchestrationService
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<UserManagementOrchestrationService> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IPersonOrchestrationService _personOrchestrationService;

        public UserManagementOrchestrationService(
            UserManager<IdentityUser> userManager,
            ILogger<UserManagementOrchestrationService> logger,
            ApplicationDbContext context,
            IPersonOrchestrationService personOrchestrationService)
        {
            _userManager = userManager;
            _logger = logger;
            _context = context;
            _personOrchestrationService = personOrchestrationService;
        }

        public async Task UpdateUserClaimsAsync(UpdateUserClaimsRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
                throw new KeyNotFoundException($"User with ID {request.UserId} not found.");

            // Update each claim
            await UpdateClaimAsync(user, "CanManageCuration", request.CanManageCuration);
            await UpdateClaimAsync(user, "CanManageUserRoles", request.CanManageUserRoles);

            _logger.LogInformation("Updated claims for user {UserId}", request.UserId);
        }

        private async Task UpdateClaimAsync(IdentityUser user, string claimType, bool hasClaim)
        {
            var existingClaim = (await _userManager.GetClaimsAsync(user))
                .FirstOrDefault(c => c.Type == claimType);

            if (hasClaim && existingClaim == null)
            {
                await _userManager.AddClaimAsync(user, new Claim(claimType, "true"));
            }
            else if (!hasClaim && existingClaim != null)
            {
                await _userManager.RemoveClaimAsync(user, existingClaim);
            }
        }

        // User CRUD operations
        public async Task<UserResponseModel?> GetCurrentUserAsync(string userId)
        {
            return await GetUserByIdAsync(userId);
        }

        public async Task<UserResponseModel> GetUserByIdAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return null;

            // Get user's recipe count from database
            // First get the person entity for this user
            var person = await _context.Persons
                .FirstOrDefaultAsync(p => p.UserId == userId);
            
            var recipeCount = 0;
            if (person != null)
            {
                recipeCount = await _context.Recipes
                    .Where(r => r.AuthorId == person.Id)
                    .CountAsync();
            }

            // Stored user claims only (granted via _GrantInitialAdminClaims.sql or user
            // management) -- deliberately not the per-household claims derived at sign-in.
            var storedClaims = await _userManager.GetClaimsAsync(user);
            var canManageCuration = storedClaims.Any(c => c.Type == "CanManageCuration" && c.Value == "true");
            var canManageUserRoles = storedClaims.Any(c => c.Type == "CanManageUserRoles" && c.Value == "true");

            return new UserResponseModel
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                LockoutEnd = user.LockoutEnd?.DateTime,
                LockoutEnabled = user.LockoutEnabled,
                AccessFailedCount = user.AccessFailedCount,
                IsActive = true, // Default value
                CreatedDate = DateTime.UtcNow, // Default value
                RecipeCount = recipeCount,
                CanManage = canManageUserRoles,
                IsAdmin = canManageCuration || canManageUserRoles
            };
        }

        public async Task<List<UserResponseModel>> GetAllUsersAsync()
        {
            var users = await _userManager.Users.ToListAsync();
            var userResponses = new List<UserResponseModel>();

            foreach (var user in users)
            {
                // Get the person entity for this user
                var person = await _context.Persons
                    .FirstOrDefaultAsync(p => p.UserId == user.Id);
                
                var recipeCount = 0;
                if (person != null)
                {
                    recipeCount = await _context.Recipes
                        .Where(r => r.AuthorId == person.Id)
                        .CountAsync();
                }

                userResponses.Add(new UserResponseModel
                {
                    Id = user.Id,
                    Username = user.UserName ?? string.Empty,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber,
                    PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                    TwoFactorEnabled = user.TwoFactorEnabled,
                    LockoutEnd = user.LockoutEnd?.DateTime,
                    LockoutEnabled = user.LockoutEnabled,
                    AccessFailedCount = user.AccessFailedCount,
                    IsActive = true, // Default value
                    CreatedDate = DateTime.UtcNow, // Default value
                    RecipeCount = recipeCount
                });
            }

            return userResponses;
        }

        public async Task<UserResponseModel> CreateUserAsync(CreateUserRequestModel request)
        {
            var user = new IdentityUser
            {
                UserName = request.Username,
                Email = request.Email,
                EmailConfirmed = false // Default to false, will be confirmed later
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            return await GetUserByIdAsync(user.Id);
        }

        public async Task<UserResponseModel> UpdateUserAsync(string userId, UpdateUserRequestModel request)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                throw new KeyNotFoundException($"User with ID {userId} not found.");

            user.UserName = request.Username ?? user.UserName;
            user.Email = request.Email ?? user.Email;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to update user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            return await GetUserByIdAsync(user.Id);
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                throw new KeyNotFoundException($"User with ID {userId} not found.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to delete user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        public async Task<List<UserRatingResponseModel>> GetUserRatingsAsync(string userId)
        {
            // First get the person entity for this user
            var person = await _context.Persons
                .FirstOrDefaultAsync(p => p.UserId == userId);
            
            if (person == null)
                return new List<UserRatingResponseModel>();

            var ratings = await _context.RecipeRatings
                .Include(r => r.Recipe)
                .Where(r => r.RaterId == person.Id)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            return ratings.Select(r => new UserRatingResponseModel
            {
                Id = r.Id,
                RecipeId = r.RecipeId,
                RecipeName = r.Recipe?.Name ?? "Unknown Recipe",
                RecipeImage = r.Recipe?.Image ?? "",
                Rating = r.Rating,
                Comment = null, // RecipeRatingEntity doesn't have a Comment property
                CreatedDate = r.CreatedDate,
                IsFavorite = r.Rating >= 4
            }).ToList();
        }

        public async Task<UserRatingResponseModel> GetUserRatingForRecipeAsync(string userId, long recipeId)
        {
            // First get the person entity for this user
            var person = await _context.Persons
                .FirstOrDefaultAsync(p => p.UserId == userId);
            
            if (person == null)
                return null;

            var rating = await _context.RecipeRatings
                .Include(r => r.Recipe)
                .FirstOrDefaultAsync(r => r.RaterId == person.Id && r.RecipeId == recipeId);

            if (rating == null)
                return null;

            return new UserRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RecipeName = rating.Recipe?.Name ?? "Unknown Recipe",
                RecipeImage = rating.Recipe?.Image ?? "",
                Rating = rating.Rating,
                Comment = null, // RecipeRatingEntity doesn't have a Comment property
                CreatedDate = rating.CreatedDate,
                IsFavorite = rating.Rating >= 4
            };
        }

        public async Task<List<UserRatingResponseModel>> GetUserFavoritesAsync(string userId)
        {
            // First get the person entity for this user
            var person = await _context.Persons
                .FirstOrDefaultAsync(p => p.UserId == userId);
            
            if (person == null)
                return new List<UserRatingResponseModel>();

            // Get recipes that the user has rated highly (4+ stars) as favorites
            var favorites = await _context.RecipeRatings
                .Include(r => r.Recipe)
                .Where(r => r.RaterId == person.Id && r.Rating >= 4)
                .OrderByDescending(r => r.Rating)
                .ThenByDescending(r => r.CreatedDate)
                .ToListAsync();

            return favorites.Select(r => new UserRatingResponseModel
            {
                Id = r.Id,
                RecipeId = r.RecipeId,
                RecipeName = r.Recipe?.Name ?? "Unknown Recipe",
                RecipeImage = r.Recipe?.Image ?? "",
                Rating = r.Rating,
                Comment = null, // RecipeRatingEntity doesn't have a Comment property
                CreatedDate = r.CreatedDate,
                IsFavorite = true
            }).ToList();
        }

        public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequestModel request)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return false;

            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            return result.Succeeded;
        }

        public async Task<bool> ForgotPasswordAsync(ForgotPasswordRequestModel request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return false;

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            // In a real implementation, you would send this token via email
            _logger.LogInformation("Password reset token generated for user {UserId}", user.Id);
            return true;
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordRequestModel request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return false;

            var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
            return result.Succeeded;
        }

        public async Task<UserResponseModel> RegisterUserAsync(RegisterUserRequestModel request)
        {
            // Use email as username if username is not provided
            var username = !string.IsNullOrWhiteSpace(request.Username) ? request.Username : request.Email;

            var user = new IdentityUser
            {
                UserName = username,
                Email = request.Email,
                EmailConfirmed = false
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to register user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            // Always create a PersonEntity for the new user
            try
            {
                var personName = !string.IsNullOrWhiteSpace(request.FullName)
                    ? request.FullName
                    : request.Email.Split('@')[0]; // Use email prefix as fallback name

                await _personOrchestrationService.SetupNewRegisteredPersonAsync(user.Id, personName);
                _logger.LogInformation("Created person for user {UserId} during registration", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create person for user {UserId} during registration", user.Id);
                // Don't fail the registration if person creation fails
            }

            return await GetUserByIdAsync(user.Id);
        }

        private const int MaxUserImageBytes = 5 * 1024 * 1024;
        private static readonly string[] UserImageExtensions = { ".jpg", ".png", ".webp" };

        private static string? DetectImageExtension(byte[] data)
        {
            if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return ".jpg";
            if (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return ".png";
            if (data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
                && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
                return ".webp";
            return null;
        }

        public async Task<string> UploadUserImageAsync(string userId, byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
                throw new ArgumentException("Image data is required.");
            if (imageData.Length > MaxUserImageBytes)
                throw new ArgumentException("Image exceeds the 5 MB size limit.");

            var extension = DetectImageExtension(imageData)
                ?? throw new ArgumentException("Unsupported image format. Only JPEG, PNG, and WebP are accepted.");

            var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "user-images");
            Directory.CreateDirectory(imagesDir);

            // Remove any previous image with a different extension before writing the new one.
            foreach (var ext in UserImageExtensions)
            {
                var stale = Path.Combine(imagesDir, $"{userId}{ext}");
                if (ext != extension && File.Exists(stale))
                    File.Delete(stale);
            }

            var fileName = $"{userId}{extension}";
            var filePath = Path.Combine(imagesDir, fileName);
            await File.WriteAllBytesAsync(filePath, imageData);

            _logger.LogInformation("Saved user image for {UserId} ({Size} bytes)", userId, imageData.Length);
            return $"/user-images/{fileName}";
        }

        public async Task<bool> DeleteUserImageAsync(string userId)
        {
            var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "user-images");
            var deleted = false;
            foreach (var ext in UserImageExtensions)
            {
                var filePath = Path.Combine(imagesDir, $"{userId}{ext}");
                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath));
                    deleted = true;
                }
            }

            if (deleted)
                _logger.LogInformation("Deleted user image for {UserId}", userId);
            return deleted;
        }

        public async Task<List<ApiTokenResponseModel>> GetUserApiTokensAsync(string userId)
        {
            return await _context.ApiTokens
                .Where(t => t.UserId == userId && t.IsActive)
                .OrderByDescending(t => t.CreatedDate)
                .Select(t => new ApiTokenResponseModel
                {
                    Id = t.Id.ToString(),
                    Name = t.Name,
                    Token = string.Empty, // Never return the hash
                    CreatedDate = t.CreatedDate,
                    LastUsedDate = t.LastUsedDate,
                    IsActive = t.IsActive
                })
                .ToListAsync();
        }

        public async Task<ApiTokenResponseModel> CreateApiTokenAsync(string userId, CreateApiTokenRequestModel request)
        {
            var rawToken = $"nom_{Guid.NewGuid():N}";
            var tokenHash = HashToken(rawToken);

            var entity = new ApiTokenEntity
            {
                UserId = userId,
                Name = request.Name,
                TokenHash = tokenHash,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            _context.ApiTokens.Add(entity);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created API token '{Name}' for user {UserId}", request.Name, userId);

            return new ApiTokenResponseModel
            {
                Id = entity.Id.ToString(),
                Name = entity.Name,
                Token = rawToken, // Only returned once at creation
                CreatedDate = entity.CreatedDate,
                LastUsedDate = null,
                IsActive = true
            };
        }

        public async Task DeleteApiTokenAsync(string userId, string tokenId)
        {
            if (!long.TryParse(tokenId, out var id))
                return;

            var token = await _context.ApiTokens
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (token != null)
            {
                token.IsActive = false;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deactivated API token {TokenId} for user {UserId}", tokenId, userId);
            }
        }

        private static string HashToken(string token)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

    }
}