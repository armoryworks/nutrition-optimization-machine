using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Claims;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class UserInfoController : BaseApiController
    {
        private readonly ILogger<UserInfoController> _logger;

        public UserInfoController(ILogger<UserInfoController> logger)
        {
            _logger = logger;
        }

        [HttpGet("current")]
        public IActionResult GetCurrentUserInfo()
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving current user info");
                return StatusCode(500, "An error occurred while retrieving user information");
            }
        }

        [HttpGet("registration-info")]
        public IActionResult GetRegistrationInfo()
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving registration info");
                return StatusCode(500, "An error occurred while retrieving registration information");
            }
        }

        [HttpGet("claims")]
        public IActionResult GetUserClaims()
        {
            try
            {
                var claims = User.Claims.Select(c => new { Type = c.Type, Value = c.Value }).ToList();
                return Ok(claims);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user claims");
                return StatusCode(500, "An error occurred while retrieving user claims");
            }
        }

        [HttpGet("has-claim")]
        public IActionResult HasClaim([FromQuery] string claimType, [FromQuery] string? claimValue = null)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user claim");
                return StatusCode(500, "An error occurred while checking user claim");
            }
        }
    }
} 