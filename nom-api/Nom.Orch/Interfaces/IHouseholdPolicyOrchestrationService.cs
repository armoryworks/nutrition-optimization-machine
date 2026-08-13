using System.Threading.Tasks;

using Nom.Orch.Models.Policy;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Steward-scoped mutations of member policies and restriction locks
    /// (household-policies design doc §§1-3). Every mutation verifies
    /// stewardship against the DATABASE (bearer-token claims are stale).
    /// These are the same tables external managers write through the policy
    /// contract; this service is the human-steward path.
    /// </summary>
    public interface IHouseholdPolicyOrchestrationService
    {
        /// <summary>The member's policy, or an empty default when none exists. Steward or self.</summary>
        Task<MemberPolicyModel> GetMemberPolicyAsync(long householdId, long personId, long requesterPersonId);

        /// <summary>Upsert the member's policy. Steward only.</summary>
        Task<MemberPolicyModel> SetMemberPolicyAsync(MemberPolicyModel model, long requesterPersonId);

        /// <summary>Lock or unlock an existing person-level restriction. Steward only; unlock also allowed to the original locker.</summary>
        Task<bool> SetRestrictionLockAsync(long householdId, long restrictionId, bool locked, long requesterPersonId);

        /// <summary>Create a (typically locked) restriction on a member. Steward only.</summary>
        Task<long> AddMemberRestrictionAsync(long householdId, long personId, StewardRestrictionRequestModel request, long requesterPersonId);
    }
}
