// File: Nom.Api/Controllers/BaseApiController.cs

using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// A base controller providing shared functionality for other API controllers,
    /// such as retrieving the authenticated user's PersonId.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseApiController : ControllerBase
    {
        /// <summary>
        /// Gets the PersonId of the currently authenticated user from their claims.
        /// Returns null if the user is in registration phase and doesn't have a PersonId yet.
        /// </summary>
        /// <returns>The user's PersonId as a long, or null if not available during registration.</returns>
        protected long? GetCurrentPersonId()
        {
            // The "PersonId" claim is added by our CustomClaimsPrincipalFactory upon user login.
            var personIdClaim = User.Claims.FirstOrDefault(c => c.Type == "PersonId")?.Value;

            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }

            // Return null instead of throwing exception - this allows for registration phase
            return null;
        }

        /// <summary>
        /// Gets the PersonId of the currently authenticated user from their claims.
        /// Throws UnauthorizedAccessException if PersonId is not available.
        /// Use this method only for endpoints that require a complete user profile.
        /// </summary>
        /// <returns>The user's PersonId as a long.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown if the PersonId claim is missing or invalid, indicating an improper authentication state.</exception>
        protected long GetCurrentPersonIdRequired()
        {
            var personId = GetCurrentPersonId();
            if (personId.HasValue)
            {
                return personId.Value;
            }

            throw new UnauthorizedAccessException("PersonId claim is missing, invalid, or could not be parsed from the user's token.");
        }

        /// <summary>
        /// Gets the registration status of the current user.
        /// </summary>
        /// <returns>The registration status as a string, or "Unknown" if not specified.</returns>
        protected string GetRegistrationStatus()
        {
            var registrationStatusClaim = User.Claims.FirstOrDefault(c => c.Type == "RegistrationStatus")?.Value;
            return registrationStatusClaim ?? "Unknown";
        }

        /// <summary>
        /// Gets the UserId of the currently authenticated user from their claims.
        /// </summary>
        /// <returns>The user's UserId as a string.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown if the UserId claim is missing or invalid.</exception>
        protected string GetCurrentUserId()
        {
            // The "sub" claim contains the user ID from Identity
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userIdClaim))
            {
                return userIdClaim;
            }

            throw new UnauthorizedAccessException("UserId claim is missing, invalid, or could not be parsed from the user's token.");
        }

        /// <summary>
        /// Gets the username of the currently authenticated user from their claims.
        /// </summary>
        /// <returns>The user's username as a string.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown if the username claim is missing or invalid.</exception>
        protected string GetCurrentUsername()
        {
            var usernameClaim = User.Claims.FirstOrDefault(c => c.Type == "name" || c.Type == ClaimTypes.Name)?.Value;

            if (!string.IsNullOrEmpty(usernameClaim))
            {
                return usernameClaim;
            }

            throw new UnauthorizedAccessException("Username claim is missing, invalid, or could not be parsed from the user's token.");
        }

        /// <summary>
        /// Gets the default household ID of the currently authenticated user from their claims.
        /// Returns null if the user doesn't have a default household set.
        /// </summary>
        /// <returns>The user's default household ID as an int, or null if not available.</returns>
        protected int? GetCurrentHouseholdId()
        {
            // The "HouseholdId" claim is added by CustomClaimsPrincipalFactory if user has a default household
            var householdIdClaim = User.Claims.FirstOrDefault(c => c.Type == "HouseholdId")?.Value;

            if (int.TryParse(householdIdClaim, out int householdId))
            {
                return householdId;
            }

            return null;
        }

        /// <summary>
        /// Returns all household IDs the current user is a member of (from HouseholdMember claims).
        /// </summary>
        protected List<long> GetUserHouseholdIds()
        {
            return User.Claims
                .Where(c => c.Type == "HouseholdMember")
                .Select(c => long.TryParse(c.Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();
        }

        /// <summary>
        /// Checks whether the current user is a member of the specified household.
        /// </summary>
        protected bool IsHouseholdMember(long householdId)
        {
            return User.Claims
                .Any(c => c.Type == "HouseholdMember" && c.Value == householdId.ToString());
        }

        /// <summary>
        /// Checks whether the current user has management permissions for the specified household.
        /// </summary>
        protected bool CanManageHousehold(long householdId)
        {
            return User.Claims
                .Any(c => c.Type == "can_manage_household" && c.Value == householdId.ToString());
        }

        /// <summary>
        /// Checks whether the current user can invite members to the specified household.
        /// </summary>
        protected bool CanInviteToHousehold(long householdId)
        {
            return User.Claims
                .Any(c => c.Type == "can_invite_household" && c.Value == householdId.ToString());
        }

        /// <summary>
        /// Checks whether the current user is an admin of the specified household.
        /// </summary>
        protected bool IsHouseholdAdmin(long householdId)
        {
            return User.Claims
                .Any(c => c.Type == "admin_household" && c.Value == householdId.ToString());
        }
    }
}