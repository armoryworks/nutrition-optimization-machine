using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Builds the household and plan claims for a person from their current, active memberships.
    /// </summary>
    public static class MembershipClaims
    {
        /// <summary>
        /// The claim types this class owns; any of them already on a principal are replaced, never merged.
        /// </summary>
        public static readonly IReadOnlySet<string> Types = new HashSet<string>
        {
            "HouseholdMember", "can_invite_household", "can_manage_household", "admin_household",
            "PlanParticipant", "can_invite_plan", "can_manage_plan", "admin_plan",
        };

        /// <summary>
        /// Loads the claims for every household the person is an active member of and every plan they take part in.
        /// </summary>
        public static async Task<List<Claim>> LoadAsync(ApplicationDbContext db, long personId)
        {
            var claims = new List<Claim>();

            var memberships = await db.HouseholdMembers
                .AsNoTracking()
                .Where(hm => hm.PersonId == personId && hm.IsActive && !hm.Household.IsDeleted)
                .Select(hm => new { hm.HouseholdId, hm.CanInvite, hm.CanManage, hm.IsAdmin })
                .ToListAsync();

            foreach (var membership in memberships)
            {
                var householdId = membership.HouseholdId.ToString();
                claims.Add(new Claim("HouseholdMember", householdId));
                if (membership.CanInvite)
                    claims.Add(new Claim("can_invite_household", householdId));
                if (membership.CanManage)
                    claims.Add(new Claim("can_manage_household", householdId));
                if (membership.IsAdmin)
                    claims.Add(new Claim("admin_household", householdId));
            }

            var participations = await db.PlanParticipants
                .AsNoTracking()
                .Where(pp => pp.PersonId == personId && !pp.Plan.IsDeleted)
                .Select(pp => new { pp.PlanId, pp.CanInvite, pp.CanManage, pp.IsAdmin })
                .ToListAsync();

            foreach (var participation in participations)
            {
                var planId = participation.PlanId.ToString();
                claims.Add(new Claim("PlanParticipant", planId));
                if (participation.CanInvite)
                    claims.Add(new Claim("can_invite_plan", planId));
                if (participation.CanManage)
                    claims.Add(new Claim("can_manage_plan", planId));
                if (participation.IsAdmin)
                    claims.Add(new Claim("admin_plan", planId));
            }

            return claims;
        }
    }
}
