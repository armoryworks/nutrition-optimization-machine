using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http; // Added for DefaultHttpContext
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
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;

namespace Nom.Orch.Services
{
    public class UserManagementOrchestrationService : IUserManagementOrchestrationService
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<UserManagementOrchestrationService> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IPersonOrchestrationService _personOrchestrationService;
        private readonly IConfiguration _configuration;

        public UserManagementOrchestrationService(
            UserManager<IdentityUser> userManager, 
            ILogger<UserManagementOrchestrationService> logger,
            ApplicationDbContext context,
            IPersonOrchestrationService personOrchestrationService,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _logger = logger;
            _context = context;
            _personOrchestrationService = personOrchestrationService;
            _configuration = configuration;
        }

        public async Task UpdateUserClaimsAsync(UpdateUserClaimsRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
                throw new Exception($"User with ID {request.UserId} not found.");

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
        public async Task<UserResponseModel> GetCurrentUserAsync()
        {
            // This would typically get the current user from the HttpContext
            // For now, we'll return a placeholder that indicates this needs to be implemented
            // based on the current request context
            _logger.LogWarning("GetCurrentUserAsync needs to be implemented with proper HttpContext access");
            
            // Simulate async operation for getting current user context
            await Task.Delay(10);
            
            return new UserResponseModel
            {
                Id = "current-user-id",
                Username = "current-user",
                UserName = "current-user",
                Email = "current@example.com",
                EmailConfirmed = true,
                PhoneNumber = null,
                PhoneNumberConfirmed = false,
                TwoFactorEnabled = false,
                LockoutEnd = null,
                LockoutEnabled = false,
                AccessFailedCount = 0,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                RecipeCount = 0
            };
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
                RecipeCount = recipeCount
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
                throw new Exception($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            return await GetUserByIdAsync(user.Id);
        }

        public async Task<UserResponseModel> UpdateUserAsync(string userId, UpdateUserRequestModel request)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                throw new Exception($"User with ID {userId} not found.");

            user.UserName = request.Username ?? user.UserName;
            user.Email = request.Email ?? user.Email;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                throw new Exception($"Failed to update user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            return await GetUserByIdAsync(user.Id);
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                throw new Exception($"User with ID {userId} not found.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new Exception($"Failed to delete user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
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

        public async Task<AuthTokenResponseModel> AuthenticateUserAsync(LoginRequestModel request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null)
            {
                _logger.LogWarning("Authentication failed: User not found for username {Username}", request.Username);
                return null;
            }

            var isValidPassword = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!isValidPassword)
            {
                _logger.LogWarning("Authentication failed: Invalid password for user {UserId}", user.Id);
                return null;
            }

            // Generate JWT token (this would need proper JWT service)
            var token = await GenerateJwtTokenAsync(user);
            
            return new AuthTokenResponseModel
            {
                AccessToken = token,
                RefreshToken = Guid.NewGuid().ToString(), // Simple refresh token
                ExpiresIn = 3600, // 1 hour
                TokenType = "Bearer"
            };
        }

        public async Task<AuthTokenResponseModel> RefreshTokenAsync()
        {
            // This would validate the refresh token and generate a new access token
            // For now, return null as this needs proper token validation
            _logger.LogWarning("RefreshTokenAsync needs to be implemented with proper token validation");
            
            // Simulate async token validation
            await Task.Delay(50);
            
            return null;
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
            _logger.LogInformation("Password reset token generated for user {UserId}: {Token}", user.Id, token);
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
                throw new Exception($"Failed to register user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            // Create person entity if FullName is provided
            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                try
                {
                    // Temporarily set the user context for person creation
                    var httpContext = new DefaultHttpContext();
                    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id)
                    }));
                    
                    // Create a temporary service scope to handle the person creation
                    // This is a workaround since we can't easily inject HttpContextAccessor here
                    var personCreateModel = new PersonCreateModel
                    {
                        PersonName = request.FullName
                    };
                    
                    // We'll need to handle this differently since we can't easily access the HttpContext here
                    // For now, we'll create the person directly in the database
                    var newPerson = new PersonEntity
                    {
                        Name = request.FullName,
                        UserId = user.Id,
                        CreatedByPersonId = 1L // System person
                    };

                    _context.Persons.Add(newPerson);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Created person {PersonId} for user {UserId} during registration", newPerson.Id, user.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create person for user {UserId} during registration", user.Id);
                    // Don't fail the registration if person creation fails
                }
            }

            return await GetUserByIdAsync(user.Id);
        }

        public async Task<bool> ValidateRegistrationTokenAsync(string token)
        {
            // This would validate a registration confirmation token
            // For now, return true as this needs proper token validation
            _logger.LogInformation("ValidateRegistrationTokenAsync called with token: {Token}", token);
            
            // Simulate async token validation
            await Task.Delay(25);
            
            return true;
        }

        public async Task<string> UploadUserImageAsync(string userId, byte[] imageData)
        {
            // This would save the image to storage and return the URL
            // For now, return a placeholder URL
            _logger.LogInformation("UploadUserImageAsync called for user {UserId} with {ImageSize} bytes", userId, imageData.Length);
            
            // Simulate async file upload
            await Task.Delay(100);
            
            return $"https://example.com/user-images/{userId}.jpg";
        }

        public async Task<bool> DeleteUserImageAsync(string userId)
        {
            // This would delete the user's image from storage
            _logger.LogInformation("DeleteUserImageAsync called for user {UserId}", userId);
            
            // Simulate async file deletion
            await Task.Delay(50);
            
            return true;
        }

        public async Task<List<ApiTokenResponseModel>> GetUserApiTokensAsync(string userId)
        {
            // This would query the database for user API tokens
            // For now, return empty list as this needs database context
            _logger.LogInformation("GetUserApiTokensAsync called for user {UserId}", userId);
            
            // Simulate async database query
            await Task.Delay(30);
            
            return new List<ApiTokenResponseModel>();
        }

        public async Task<ApiTokenResponseModel> CreateApiTokenAsync(string userId, CreateApiTokenRequestModel request)
        {
            // This would create and store an API token
            // For now, return a mock token
            _logger.LogInformation("CreateApiTokenAsync called for user {UserId} with name {TokenName}", userId, request.Name);
            
            // Simulate async token creation
            await Task.Delay(75);
            
            return new ApiTokenResponseModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Token = $"api_token_{Guid.NewGuid():N}",
                CreatedDate = DateTime.UtcNow,
                LastUsedDate = null,
                IsActive = true
            };
        }

        public async Task DeleteApiTokenAsync(string userId, string tokenId)
        {
            // This would delete the API token from storage
            _logger.LogInformation("DeleteApiTokenAsync called for user {UserId} and token {TokenId}", userId, tokenId);
            
            // Simulate async token deletion
            await Task.Delay(25);
        }

        private async Task<string> GenerateJwtTokenAsync(IdentityUser user)
        {
            try
            {
                // Get user claims
                var claims = await _userManager.GetClaimsAsync(user);
                
                // Add standard claims
                var standardClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, user.UserName ?? ""),
                    new Claim(ClaimTypes.Email, user.Email ?? ""),
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
                };

                // Combine user claims with standard claims
                var allClaims = standardClaims.Concat(claims).ToList();

                // Create JWT token
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("your-secret-key-here-minimum-32-characters"));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: "NOM-API",
                    audience: "NOM-Client",
                    claims: allClaims,
                    expires: DateTime.UtcNow.AddHours(24),
                    signingCredentials: creds
                );

                return new JwtSecurityTokenHandler().WriteToken(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating JWT token for user {UserId}", user.Id);
                throw;
            }
        }
    }
}