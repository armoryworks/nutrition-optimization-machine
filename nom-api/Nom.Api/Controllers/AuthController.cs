using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserManagementOrchestrationService _userService;

        public AuthController(IUserManagementOrchestrationService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Re-issues the bearer token with fresh claims from the database.
        /// Call after any membership change (household create/join, plan participation).
        /// </summary>
        [Authorize]
        [HttpPost("refresh-claims")]
        public async Task<IActionResult> RefreshClaims(
            [FromServices] SignInManager<IdentityUser> signInManager,
            [FromServices] UserManager<IdentityUser> userManager)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // CreateUserPrincipalAsync triggers CustomClaimsPrincipalFactory
            var principal = await signInManager.CreateUserPrincipalAsync(user);

            // SignIn with bearer scheme writes new token to response body
            return SignIn(principal, IdentityConstants.BearerScheme);
        }

        [HttpPost("forgotPassword")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestModel request)
        {
            // Always return OK regardless of whether the email exists,
            // to avoid revealing which emails are registered.
            await _userService.ForgotPasswordAsync(request);
            return Ok();
        }

        [HttpPost("resetPassword")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestModel request)
        {
            var success = await _userService.ResetPasswordAsync(request);
            if (!success)
            {
                return BadRequest(new { message = "Unable to reset password. The link may have expired." });
            }
            return Ok();
        }
    }
}
