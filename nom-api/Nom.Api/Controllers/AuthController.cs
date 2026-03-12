using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nom.Api.Settings;
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

        // refresh-claims moved to minimal API endpoint in Program.cs for proper bearer token response

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

        [HttpPost("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(
            [FromBody] ConfirmEmailRequest request,
            [FromServices] UserManager<IdentityUser> userManager)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { message = "UserId and Token are required." });
            }

            var user = await userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                // Don't reveal that user doesn't exist
                return BadRequest(new { message = "Invalid confirmation request." });
            }

            var result = await userManager.ConfirmEmailAsync(user, request.Token);
            if (!result.Succeeded)
            {
                return BadRequest(new { message = "Email confirmation failed. The link may have expired." });
            }

            return Ok(new { message = "Email confirmed successfully." });
        }

        [HttpPost("resend-confirmation")]
        [AllowAnonymous]
        public async Task<IActionResult> ResendConfirmation(
            [FromBody] ResendConfirmationRequest request,
            [FromServices] UserManager<IdentityUser> userManager,
            [FromServices] IEmailSender<IdentityUser> emailSender,
            [FromServices] IOptions<FrontendSettings> frontendSettings)
        {
            // Always return OK to avoid revealing which emails are registered
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Ok(new { message = "If an account exists with this email, a confirmation link has been sent." });
            }

            var user = await userManager.FindByEmailAsync(request.Email);
            if (user != null && !user.EmailConfirmed)
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var frontendUrl = frontendSettings.Value.Url;
                var confirmLink = $"{frontendUrl}/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
                await emailSender.SendConfirmationLinkAsync(user, request.Email, confirmLink);
            }

            return Ok(new { message = "If an account exists with this email, a confirmation link has been sent." });
        }

        // 2FA Management Endpoints

        [Authorize]
        [HttpPost("2fa/setup")]
        public async Task<ActionResult<TwoFactorSetupResponseModel>> Setup2FA(
            [FromServices] UserManager<IdentityUser> userManager)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            // Reset the authenticator key to generate a new one
            await userManager.ResetAuthenticatorKeyAsync(user);
            var key = await userManager.GetAuthenticatorKeyAsync(user);

            if (string.IsNullOrEmpty(key))
                return StatusCode(500, new { message = "Failed to generate authenticator key" });

            var email = await userManager.GetEmailAsync(user);
            var uri = $"otpauth://totp/NOM:{Uri.EscapeDataString(email ?? user.UserName ?? "user")}?secret={key}&issuer=NOM";

            return Ok(new TwoFactorSetupResponseModel
            {
                SharedKey = FormatKey(key),
                AuthenticatorUri = uri
            });
        }

        [Authorize]
        [HttpPost("2fa/verify")]
        public async Task<ActionResult<TwoFactorRecoveryCodesModel>> Verify2FA(
            [FromBody] TwoFactorVerifyRequest request,
            [FromServices] UserManager<IdentityUser> userManager)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var isValid = await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                request.Code);

            if (!isValid)
                return BadRequest(new { message = "Invalid verification code." });

            await userManager.SetTwoFactorEnabledAsync(user, true);
            var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            return Ok(new TwoFactorRecoveryCodesModel
            {
                RecoveryCodes = recoveryCodes?.ToArray() ?? Array.Empty<string>()
            });
        }

        [Authorize]
        [HttpPost("2fa/disable")]
        public async Task<IActionResult> Disable2FA(
            [FromBody] TwoFactorVerifyRequest request,
            [FromServices] UserManager<IdentityUser> userManager)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            // Verify the code before disabling
            var isValid = await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                request.Code);

            if (!isValid)
                return BadRequest(new { message = "Invalid verification code." });

            await userManager.SetTwoFactorEnabledAsync(user, false);
            await userManager.ResetAuthenticatorKeyAsync(user);

            return Ok(new { message = "2FA has been disabled." });
        }

        [Authorize]
        [HttpPost("2fa/recovery-codes")]
        public async Task<ActionResult<TwoFactorRecoveryCodesModel>> GenerateRecoveryCodes(
            [FromBody] TwoFactorVerifyRequest request,
            [FromServices] UserManager<IdentityUser> userManager)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            if (!await userManager.GetTwoFactorEnabledAsync(user))
                return BadRequest(new { message = "2FA is not enabled." });

            // Verify current code before generating new recovery codes
            var isValid = await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                request.Code);

            if (!isValid)
                return BadRequest(new { message = "Invalid verification code." });

            var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            return Ok(new TwoFactorRecoveryCodesModel
            {
                RecoveryCodes = recoveryCodes?.ToArray() ?? Array.Empty<string>()
            });
        }

        [Authorize]
        [HttpGet("2fa/status")]
        public async Task<ActionResult<TwoFactorStatusModel>> Get2FAStatus(
            [FromServices] UserManager<IdentityUser> userManager)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var isEnabled = await userManager.GetTwoFactorEnabledAsync(user);
            var hasAuthenticator = !string.IsNullOrEmpty(await userManager.GetAuthenticatorKeyAsync(user));
            var recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);

            return Ok(new TwoFactorStatusModel
            {
                IsEnabled = isEnabled,
                HasAuthenticator = hasAuthenticator,
                RecoveryCodesLeft = recoveryCodesLeft
            });
        }

        private static string FormatKey(string unformattedKey)
        {
            var result = new System.Text.StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
            {
                result.Append(unformattedKey.AsSpan(currentPosition));
            }
            return result.ToString().ToLowerInvariant();
        }
    }
}
