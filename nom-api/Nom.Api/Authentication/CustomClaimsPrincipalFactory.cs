// File: Nom.Api/Authentication/CustomClaimsPrincipalFactory.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.Tasks;
using Nom.Data;
using Nom.Data.Person;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Custom Claims Principal Factory to add comprehensive claims based on Mealie's permission system.
    /// This links the authentication identity (IdentityUser) with the application's user profile (PersonEntity)
    /// and includes all user permissions and roles as claims.
    /// </summary>
    public class CustomClaimsPrincipalFactory : UserClaimsPrincipalFactory<IdentityUser>
    {
        private readonly ApplicationDbContext _dbContext;

        public CustomClaimsPrincipalFactory(
            UserManager<IdentityUser> userManager,
            IOptions<IdentityOptions> optionsAccessor,
            ApplicationDbContext dbContext)
            : base(userManager, optionsAccessor)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Overrides the default claim generation to add comprehensive claims including:
        /// - PersonId (links to PersonEntity) - only if Person entity exists
        /// - RegistrationStatus (indicates user's registration phase)
        /// - User permissions (can_invite, can_manage, can_manage_household, can_organize, admin)
        /// - Group and household information
        /// - Role-based claims
        /// </summary>
        /// <param name="user">The IdentityUser for whom to generate claims.</param>
        /// <returns>A ClaimsIdentity containing all standard claims plus custom application claims.</returns>
        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(IdentityUser user)
        {
            // Get the default claims from the base implementation (e.g., sub, email, name)
            var identity = await base.GenerateClaimsAsync(user);

            // Find the associated PersonEntity
            var person = await _dbContext.Persons
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (person != null)
            {
                // Add PersonId claim
                identity.AddClaim(new Claim("PersonId", person.Id.ToString()));
                
                // Add registration status claim
                identity.AddClaim(new Claim("RegistrationStatus", "Complete"));

                identity.AddClaims(await MembershipClaims.LoadAsync(_dbContext, person.Id));

                // System-wide permissions (CanManageCuration, CanManageUserRoles) come only
                // from stored user claims included by the base factory. Household/plan admin
                // roles are tenant-scoped and must never grant global claims.
            }
            else
            {
                // User exists but no Person entity yet - this is the registration phase
                identity.AddClaim(new Claim("RegistrationStatus", "InProgress"));
                // Note: No PersonId claim is added in this case
            }

            return identity;
        }
    }
}