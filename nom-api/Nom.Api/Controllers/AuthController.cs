using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : BaseApiController
    {
        private readonly IUserManagementOrchestrationService _userService;

        public AuthController(IUserManagementOrchestrationService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Authenticate user and get access token
        /// </summary>
        [HttpPost("token")]
        public async Task<ActionResult<AuthTokenResponseModel>> GetToken([FromBody] LoginRequestModel request)
        {
            try
            {
                var token = await _userService.AuthenticateUserAsync(request);
                return Ok(token);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Authentication failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Refresh access token
        /// </summary>
        [HttpPost("refresh")]
        public async Task<ActionResult<AuthTokenResponseModel>> RefreshToken()
        {
            try
            {
                var token = await _userService.RefreshTokenAsync();
                return Ok(token);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Token refresh failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Register new user
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult<UserResponseModel>> Register([FromBody] RegisterUserRequestModel request)
        {
            try
            {
                var user = await _userService.RegisterUserAsync(request);
                return CreatedAtAction(nameof(Register), user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Registration failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Validate registration token
        /// </summary>
        [HttpGet("register/validate/{token}")]
        public async Task<ActionResult<bool>> ValidateRegistrationToken(string token)
        {
            try
            {
                var isValid = await _userService.ValidateRegistrationTokenAsync(token);
                return Ok(new { isValid });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Token validation failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Request password reset
        /// </summary>
        [HttpPost("forgot-password")]
        public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequestModel request)
        {
            try
            {
                var success = await _userService.ForgotPasswordAsync(request);
                if (!success)
                {
                    return BadRequest(new { message = "Failed to send password reset email" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Password reset request failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Reset password with token
        /// </summary>
        [HttpPost("reset-password")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequestModel request)
        {
            try
            {
                var success = await _userService.ResetPasswordAsync(request);
                if (!success)
                {
                    return BadRequest(new { message = "Failed to reset password" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Password reset failed", error = ex.Message });
            }
        }
    }
} 