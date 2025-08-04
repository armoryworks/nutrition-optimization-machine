// File: Nom.Api/Authentication/CustomClaimsPrincipalFactory.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Custom Claims Principal Factory to add comprehensive claims based on Mealie's permission system.
    /// This links the authentication identity (IdentityUser) with the application's user profile (PersonEntity)
    /// and includes all user permissions and roles as claims.
    /// </summary>
    public class CustomClaimsPrincipalFactory : UserClaimsPrincipalFactory<IdentityUser>
    {
        public CustomClaimsPrincipalFactory(
            UserManager<IdentityUser> userManager,
            IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, optionsAccessor)
        {
        }

        /// <summary>
        /// Overrides the default claim generation to add comprehensive claims including:
        /// - PersonId (links to PersonEntity)
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

            // TODO: Implement custom claims when PersonEntity model is finalized
            // For now, just return the base claims

            /**can_invite, can_manage, can_manage_household, can_organize, or admin  
            ** are going to be contextually based on the type of relationship the personentity has to the 
            ** this will make it so perhaps we have a can_<doathing>_household, can_<doathing>_plan
            ** plan or household 
            **/

            return identity;
        }
    }
}