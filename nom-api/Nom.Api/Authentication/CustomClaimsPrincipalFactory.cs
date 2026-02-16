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

                // Get household memberships to determine permissions
                var householdMemberships = await _dbContext.HouseholdMembers
                    .Where(hm => hm.PersonId == person.Id)
                    .Include(hm => hm.Household)
                    .ToListAsync();

                // Get plan participations to determine plan-level permissions
                var planParticipations = await _dbContext.PlanParticipants
                    .Where(pp => pp.PersonId == person.Id)
                    .Include(pp => pp.Plan)
                    .ToListAsync();

                // Add household-specific permissions
                foreach (var membership in householdMemberships)
                {
                    var householdId = membership.HouseholdId.ToString();
                    
                    // Add household membership claim
                    identity.AddClaim(new Claim("HouseholdMember", householdId));

                    // Add household-specific permissions based on role
                    if (membership.CanInvite)
                        identity.AddClaim(new Claim("can_invite_household", householdId));
                    
                    if (membership.CanManage)
                        identity.AddClaim(new Claim("can_manage_household", householdId));
                    
                    if (membership.IsAdmin)
                        identity.AddClaim(new Claim("admin_household", householdId));
                }

                // Add plan-specific permissions
                foreach (var participation in planParticipations)
                {
                    var planId = participation.PlanId.ToString();
                    
                    // Add plan participation claim
                    identity.AddClaim(new Claim("PlanParticipant", planId));

                    // Add plan-specific permissions based on role
                    if (participation.CanInvite)
                        identity.AddClaim(new Claim("can_invite_plan", planId));
                    
                    if (participation.CanManage)
                        identity.AddClaim(new Claim("can_manage_plan", planId));
                    
                    if (participation.IsAdmin)
                        identity.AddClaim(new Claim("admin_plan", planId));
                }

                // Add global permissions if user has any admin roles
                if (householdMemberships.Any(hm => hm.IsAdmin) || 
                    planParticipations.Any(pp => pp.IsAdmin))
                {
                    identity.AddClaim(new Claim("admin", "true"));
                }

                // Add curation permissions if user has any management roles
                if (householdMemberships.Any(hm => hm.CanManage) ||
                    planParticipations.Any(pp => pp.CanManage))
                {
                    identity.AddClaim(new Claim("CanManageCuration", "true"));
                }

                // Add user management permissions if user is admin anywhere
                if (householdMemberships.Any(hm => hm.IsAdmin) ||
                    planParticipations.Any(pp => pp.IsAdmin))
                {
                    identity.AddClaim(new Claim("CanManageUserRoles", "true"));
                }
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