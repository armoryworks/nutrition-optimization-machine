using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Nom.Data;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Replaces the household and plan claims carried in a sign-in token with the person's memberships as they
    /// stand now, so leaving or being removed from a household takes effect on the next request rather than at
    /// the next sign-in.
    /// </summary>
    public class MembershipClaimsTransformation : IClaimsTransformation
    {
        private readonly ApplicationDbContext _dbContext;

        public MembershipClaimsTransformation(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Returns a copy of the principal whose membership claims come from the database.
        /// </summary>
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } ||
                !long.TryParse(principal.FindFirst("PersonId")?.Value, out var personId))
            {
                return principal;
            }

            var fresh = await MembershipClaims.LoadAsync(_dbContext, personId);

            var transformed = new ClaimsPrincipal(principal.Identities.Select(i => i.Clone()));
            foreach (var identity in transformed.Identities)
            {
                foreach (var stale in identity.Claims.Where(c => MembershipClaims.Types.Contains(c.Type)).ToList())
                {
                    identity.RemoveClaim(stale);
                }
            }
            ((ClaimsIdentity)transformed.Identity!).AddClaims(fresh);

            return transformed;
        }
    }
}
