using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Policy;

namespace Nom.Orch.Services
{
    public class HouseholdPolicyOrchestrationService : IHouseholdPolicyOrchestrationService
    {
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _context;
        private readonly IPolicyEnforcementService _policy;

        public HouseholdPolicyOrchestrationService(ApplicationDbContext context, IPolicyEnforcementService policy)
        {
            _context = context;
            _policy = policy;
        }

        public async Task<MemberPolicyModel> GetMemberPolicyAsync(long householdId, long personId, long requesterPersonId)
        {
            if (requesterPersonId != personId && !await _policy.IsStewardAsync(requesterPersonId, householdId))
            {
                throw new UnauthorizedAccessException("Only a household steward may view another member's policy.");
            }

            var entity = await _context.MemberPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.HouseholdId == householdId && p.PersonId == personId);

            return entity == null
                ? new MemberPolicyModel { HouseholdId = householdId, PersonId = personId }
                : ToModel(entity);
        }

        public async Task<MemberPolicyModel> SetMemberPolicyAsync(MemberPolicyModel model, long requesterPersonId)
        {
            if (!await _policy.IsStewardAsync(requesterPersonId, model.HouseholdId))
            {
                throw new UnauthorizedAccessException("Only a household steward may set member policies.");
            }

            var isMember = await _context.HouseholdMembers.AnyAsync(hm =>
                hm.HouseholdId == model.HouseholdId && hm.PersonId == model.PersonId && hm.IsActive);
            if (!isMember)
            {
                throw new InvalidOperationException("The person is not an active member of the household.");
            }

            var entity = await _context.MemberPolicies
                .FirstOrDefaultAsync(p => p.HouseholdId == model.HouseholdId && p.PersonId == model.PersonId);
            if (entity == null)
            {
                entity = new MemberPolicyEntity
                {
                    HouseholdId = model.HouseholdId,
                    PersonId = model.PersonId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = requesterPersonId,
                };
                _context.MemberPolicies.Add(entity);
            }

            entity.FeatureGates = JsonSerializer.Serialize(model.FeatureGates ?? new Dictionary<string, bool>(), JsonOpts);
            entity.FrequencyCaps = JsonSerializer.Serialize(model.FrequencyCaps ?? new List<FrequencyCapModel>(), JsonOpts);
            entity.CuratedOnly = model.CuratedOnly;
            entity.UpdatedBy = $"person:{requesterPersonId}";

            await _context.SaveChangesAsync();
            return ToModel(entity);
        }

        public async Task<bool> SetRestrictionLockAsync(long householdId, long restrictionId, bool locked, long requesterPersonId)
        {
            var restriction = await _context.Restrictions
                .FirstOrDefaultAsync(r => r.Id == restrictionId && r.PlanId == null);
            if (restriction?.PersonId == null)
            {
                return false;
            }

            var targetInHousehold = await _context.HouseholdMembers.AnyAsync(hm =>
                hm.HouseholdId == householdId && hm.PersonId == restriction.PersonId && hm.IsActive);
            if (!targetInHousehold)
            {
                return false;
            }

            if (!await _policy.IsStewardAsync(requesterPersonId, householdId))
            {
                throw new UnauthorizedAccessException("Only a household steward may change restriction locks.");
            }

            // Managed locks are the manager's: a steward may not silently
            // unlock what an external manager (e.g. a provider) locked — that
            // path is disenrollment/suspension, where locks convert to steward
            // control deliberately.
            if (!locked && restriction.Locked
                && restriction.LockedBy != null && !restriction.LockedBy.StartsWith("person:"))
            {
                throw new UnauthorizedAccessException("This restriction is locked by an external manager and cannot be unlocked here.");
            }

            restriction.Locked = locked;
            restriction.LockedBy = locked ? $"person:{requesterPersonId}" : null;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<long> AddMemberRestrictionAsync(long householdId, long personId, StewardRestrictionRequestModel request, long requesterPersonId)
        {
            if (!await _policy.IsStewardAsync(requesterPersonId, householdId))
            {
                throw new UnauthorizedAccessException("Only a household steward may add member restrictions.");
            }

            var isMember = await _context.HouseholdMembers.AnyAsync(hm =>
                hm.HouseholdId == householdId && hm.PersonId == personId && hm.IsActive);
            if (!isMember)
            {
                throw new InvalidOperationException("The person is not an active member of the household.");
            }

            var entity = new Nom.Data.Plan.RestrictionEntity
            {
                PersonId = personId,
                PlanId = null,
                Name = request.Name,
                Description = request.Description,
                RestrictionTypeId = request.RestrictionTypeId,
                IngredientId = request.IngredientId,
                NutrientId = request.NutrientId,
                Severity = request.Severity,
                Locked = request.Locked,
                LockedBy = request.Locked ? $"person:{requesterPersonId}" : null,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = requesterPersonId,
            };
            _context.Restrictions.Add(entity);
            await _context.SaveChangesAsync();
            return entity.Id;
        }

        private static MemberPolicyModel ToModel(MemberPolicyEntity entity)
        {
            Dictionary<string, bool> gates;
            List<FrequencyCapModel> caps;
            try { gates = JsonSerializer.Deserialize<Dictionary<string, bool>>(entity.FeatureGates, JsonOpts) ?? new(); }
            catch (JsonException) { gates = new(); }
            try { caps = JsonSerializer.Deserialize<List<FrequencyCapModel>>(entity.FrequencyCaps, JsonOpts) ?? new(); }
            catch (JsonException) { caps = new(); }

            return new MemberPolicyModel
            {
                HouseholdId = entity.HouseholdId,
                PersonId = entity.PersonId,
                FeatureGates = gates,
                FrequencyCaps = caps,
                CuratedOnly = entity.CuratedOnly,
                UpdatedBy = entity.UpdatedBy,
            };
        }
    }
}
