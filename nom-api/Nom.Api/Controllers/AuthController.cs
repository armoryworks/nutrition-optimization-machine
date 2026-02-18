using Microsoft.AspNetCore.Authorization;
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
