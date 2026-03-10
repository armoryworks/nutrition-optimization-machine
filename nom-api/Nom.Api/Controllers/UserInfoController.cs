using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Security.Claims;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class UserInfoController : BaseApiController
    {
        [HttpGet("current")]
        public IActionResult GetCurrentUserInfo()
        {
            var personId = GetCurrentPersonId();
            var registrationStatus = GetRegistrationStatus();
            var claims = User.Claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();

            // Handle registration phase (no PersonId yet)
            if (!personId.HasValue)
            {
                var registrationInfo = new
                {
                    Status = "RegistrationInProgress",
                    RegistrationStatus = registrationStatus,
                    UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    Email = User.FindFirst(ClaimTypes.Email)?.Value,
                    UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                    PersonId = (long?)null,
                    Claims = claims
                };

                return Ok(registrationInfo);
            }

            // Handle complete user (with PersonId)
            var userInfo = new
            {
                Status = "Complete",
                RegistrationStatus = registrationStatus,
                PersonId = personId.Value,
                HouseholdId = GetCurrentHouseholdId(),
                UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Email = User.FindFirst(ClaimTypes.Email)?.Value,
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                Claims = claims
            };

            return Ok(userInfo);
        }

        [HttpGet("registration-info")]
        public IActionResult GetRegistrationInfo()
        {
            var personId = GetCurrentPersonId();
            var registrationStatus = GetRegistrationStatus();
            var claims = User.Claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();

            // This endpoint is specifically for registration phase
            var registrationInfo = new
            {
                Status = personId.HasValue ? "Complete" : "RegistrationInProgress",
                RegistrationStatus = registrationStatus,
                UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Email = User.FindFirst(ClaimTypes.Email)?.Value,
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                PersonId = personId,
                Claims = claims,
                IsRegistrationComplete = personId.HasValue
            };

            return Ok(registrationInfo);
        }

        [HttpGet("claims")]
        public IActionResult GetUserClaims()
        {
            var claims = User.Claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();
            return Ok(claims);
        }

        [HttpGet("has-claim")]
        public IActionResult HasClaim([FromQuery] string claimType, [FromQuery] string? claimValue = null)
        {
            bool hasClaim;
            if (string.IsNullOrEmpty(claimValue))
            {
                hasClaim = User.HasClaim(c => c.Type == claimType);
            }
            else
            {
                hasClaim = User.HasClaim(c => c.Type == claimType && c.Value == claimValue);
            }

            return Ok(new { HasClaim = hasClaim });
        }
    }
}