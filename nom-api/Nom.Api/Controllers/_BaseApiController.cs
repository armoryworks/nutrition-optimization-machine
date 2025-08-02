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
        /// </summary>
        /// <returns>The user's PersonId as a long.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown if the PersonId claim is missing or invalid, indicating an improper authentication state.</exception>
        protected long GetCurrentPersonId()
        {
            // The "PersonId" claim is added by our CustomClaimsPrincipalFactory upon user login.
            var personIdClaim = User.Claims.FirstOrDefault(c => c.Type == "PersonId")?.Value;

            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }

            // as the claim is essential for a valid session.
            throw new UnauthorizedAccessException("PersonId claim is missing, invalid, or could not be parsed from the user's token.");
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
    }
}