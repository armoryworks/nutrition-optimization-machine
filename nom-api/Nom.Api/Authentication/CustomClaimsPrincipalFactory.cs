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

            // Query the database to find the PersonEntity and related data
            var person = await _dbContext.Persons
                .Include(p => p.Group)
                .Include(p => p.Household)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (person != null)
            {
                // Add PersonId claim (links IdentityUser to PersonEntity)
                identity.AddClaim(new Claim("PersonId", person.Id.ToString()));

                // Add user permission claims (from Mealie's permission system)
                identity.AddClaim(new Claim("CanInvite", person.CanInvite.ToString().ToLower()));
                identity.AddClaim(new Claim("CanManage", person.CanManage.ToString().ToLower()));
                identity.AddClaim(new Claim("CanManageHousehold", person.CanManageHousehold.ToString().ToLower()));
                identity.AddClaim(new Claim("CanOrganize", person.CanOrganize.ToString().ToLower()));
                identity.AddClaim(new Claim("IsAdmin", person.IsAdmin.ToString().ToLower()));

                // Add group and household information
                if (person.GroupId.HasValue)
                {
                    identity.AddClaim(new Claim("GroupId", person.GroupId.Value.ToString()));
                    identity.AddClaim(new Claim("GroupName", person.Group?.Name ?? "Unknown"));
                }

                if (person.HouseholdId.HasValue)
                {
                    identity.AddClaim(new Claim("HouseholdId", person.HouseholdId.Value.ToString()));
                    identity.AddClaim(new Claim("HouseholdName", person.Household?.Name ?? "Unknown"));
                }

                // Add role-based claims
                var roles = new List<string>();

                // Admin role
                if (person.IsAdmin)
                {
                    roles.Add("Admin");
                    identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
                }

                // Manager role
                if (person.CanManage)
                {
                    roles.Add("Manager");
                    identity.AddClaim(new Claim(ClaimTypes.Role, "Manager"));
                }

                // Household Manager role
                if (person.CanManageHousehold)
                {
                    roles.Add("HouseholdManager");
                    identity.AddClaim(new Claim(ClaimTypes.Role, "HouseholdManager"));
                }

                // Organizer role
                if (person.CanOrganize)
                {
                    roles.Add("Organizer");
                    identity.AddClaim(new Claim(ClaimTypes.Role, "Organizer"));
                }

                // Inviter role
                if (person.CanInvite)
                {
                    roles.Add("Inviter");
                    identity.AddClaim(new Claim(ClaimTypes.Role, "Inviter"));
                }

                // Default user role
                roles.Add("User");
                identity.AddClaim(new Claim(ClaimTypes.Role, "User"));

                // Add roles as a single claim for easy access
                identity.AddClaim(new Claim("Roles", string.Join(",", roles)));

                // Add application-specific claims
                identity.AddClaim(new Claim("Application", "NOM"));
                identity.AddClaim(new Claim("Version", "1.0"));
            }

            return identity;
        }
    }
}
