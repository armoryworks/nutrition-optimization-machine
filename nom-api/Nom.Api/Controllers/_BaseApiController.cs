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
        /// * Gets the PersonId of the currently authenticated user from their claims.
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

            // This should theoretically never be reached for an endpoint protected by [Authorize],
            // as the claim is essential for a valid session.
            throw new UnauthorizedAccessException("PersonId claim is missing, invalid, or could not be parsed from the user's token.");
        }
    }
}