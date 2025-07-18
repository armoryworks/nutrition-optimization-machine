// File: Nom.Api/Authentication/CustomClaimsPrincipalFactory.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nom.Data;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Custom Claims Principal Factory to add the PersonId to the user's claims upon login.
    /// This links the authentication identity (IdentityUser) with the application's user profile (PersonEntity).
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
        /// Overrides the default claim generation to add the custom "PersonId" claim.
        /// </summary>
        /// <param name="user">The IdentityUser for whom to generate claims.</param>
        /// <returns>A ClaimsIdentity containing all standard claims plus the new PersonId claim.</returns>
        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(IdentityUser user)
        {
            // Get the default claims from the base implementation (e.g., sub, email)
            var identity = await base.GenerateClaimsAsync(user);

            // Query the database to find the PersonEntity linked to this IdentityUser
            var person = await _dbContext.Persons
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            // If a corresponding PersonEntity is found, add its ID as a claim
            if (person != null)
            {
                identity.AddClaim(new Claim("PersonId", person.Id.ToString()));
            }

            return identity;
        }
    }
}
