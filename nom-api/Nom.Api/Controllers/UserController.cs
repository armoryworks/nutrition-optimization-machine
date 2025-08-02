using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserController : BaseApiController
    {
        private readonly IUserManagementOrchestrationService _userService;

        public UserController(IUserManagementOrchestrationService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Get current user information
        /// </summary>
        [HttpGet("self")]
        public async Task<ActionResult<UserResponseModel>> GetCurrentUser()
        {
            try
            {
                var user = await _userService.GetCurrentUserAsync();
                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get current user", error = ex.Message });
            }
        }

        /// <summary>
        /// Get user by ID (admin only)
        /// </summary>
        [HttpGet("{userId}")]
        [Authorize(Policy = "CanManageUserRoles")]
        public async Task<ActionResult<UserResponseModel>> GetUserById(string userId)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }
                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get user", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all users (admin only)
        /// </summary>
        [HttpGet]
        [Authorize(Policy = "CanManageUserRoles")]
        public async Task<ActionResult<List<UserResponseModel>>> GetAllUsers()
        {
            try
            {
                var users = await _userService.GetAllUsersAsync();
                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get users", error = ex.Message });
            }
        }

        /// <summary>
        /// Create new user (admin only)
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "CanManageUserRoles")]
        public async Task<ActionResult<UserResponseModel>> CreateUser([FromBody] CreateUserRequestModel request)
        {
            try
            {
                var user = await _userService.CreateUserAsync(request);
                return CreatedAtAction(nameof(GetUserById), new { userId = user.Id }, user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create user", error = ex.Message });
            }
        }

        /// <summary>
        /// Update user
        /// </summary>
        [HttpPut("{userId}")]
        public async Task<ActionResult<UserResponseModel>> UpdateUser(string userId, [FromBody] UpdateUserRequestModel request)
        {
            try
            {
                var user = await _userService.UpdateUserAsync(userId, request);
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }
                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update user", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete user (admin only)
        /// </summary>
        [HttpDelete("{userId}")]
        [Authorize(Policy = "CanManageUserRoles")]
        public async Task<ActionResult> DeleteUser(string userId)
        {
            try
            {
                await _userService.DeleteUserAsync(userId);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete user", error = ex.Message });
            }
        }

        /// <summary>
        /// Get user ratings
        /// </summary>
        [HttpGet("self/ratings")]
        public async Task<ActionResult<List<UserRatingResponseModel>>> GetUserRatings()
        {
            try
            {
                var userId = GetCurrentUserId();
                var ratings = await _userService.GetUserRatingsAsync(userId);
                return Ok(ratings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get user ratings", error = ex.Message });
            }
        }

        /// <summary>
        /// Get user rating for specific recipe
        /// </summary>
        [HttpGet("self/ratings/{recipeId}")]
        public async Task<ActionResult<UserRatingResponseModel>> GetUserRatingForRecipe(long recipeId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var rating = await _userService.GetUserRatingForRecipeAsync(userId, recipeId);
                if (rating == null)
                {
                    return NotFound(new { message = "User has not rated this recipe" });
                }
                return Ok(rating);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get user rating", error = ex.Message });
            }
        }

        /// <summary>
        /// Get user favorites
        /// </summary>
        [HttpGet("self/favorites")]
        public async Task<ActionResult<List<UserRatingResponseModel>>> GetUserFavorites()
        {
            try
            {
                var userId = GetCurrentUserId();
                var favorites = await _userService.GetUserFavoritesAsync(userId);
                return Ok(favorites);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get user favorites", error = ex.Message });
            }
        }

        /// <summary>
        /// Change password
        /// </summary>
        [HttpPut("password")]
        public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequestModel request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var success = await _userService.ChangePasswordAsync(userId, request);
                if (!success)
                {
                    return BadRequest(new { message = "Failed to change password" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to change password", error = ex.Message });
            }
        }

        /// <summary>
        /// Upload user image
        /// </summary>
        [HttpPost("image")]
        public async Task<ActionResult<string>> UploadUserImage([FromBody] byte[] imageData)
        {
            try
            {
                var userId = GetCurrentUserId();
                var imageUrl = await _userService.UploadUserImageAsync(userId, imageData);
                return Ok(new { imageUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to upload user image", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete user image
        /// </summary>
        [HttpDelete("image")]
        public async Task<ActionResult> DeleteUserImage()
        {
            try
            {
                var userId = GetCurrentUserId();
                var success = await _userService.DeleteUserImageAsync(userId);
                if (!success)
                {
                    return BadRequest(new { message = "Failed to delete user image" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete user image", error = ex.Message });
            }
        }

        /// <summary>
        /// Get user API tokens
        /// </summary>
        [HttpGet("api-tokens")]
        public async Task<ActionResult<List<ApiTokenResponseModel>>> GetUserApiTokens()
        {
            try
            {
                var userId = GetCurrentUserId();
                var tokens = await _userService.GetUserApiTokensAsync(userId);
                return Ok(tokens);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to get API tokens", error = ex.Message });
            }
        }

        /// <summary>
        /// Create API token
        /// </summary>
        [HttpPost("api-tokens")]
        public async Task<ActionResult<ApiTokenResponseModel>> CreateApiToken([FromBody] CreateApiTokenRequestModel request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var token = await _userService.CreateApiTokenAsync(userId, request);
                return CreatedAtAction(nameof(GetUserApiTokens), token);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create API token", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete API token
        /// </summary>
        [HttpDelete("api-tokens/{tokenId}")]
        public async Task<ActionResult> DeleteApiToken(string tokenId)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _userService.DeleteApiTokenAsync(userId, tokenId);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete API token", error = ex.Message });
            }
        }
    }
} 