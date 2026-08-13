using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Nom.Orch.Interfaces;
using Nom.Orch.Models.Policy;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Steward-scoped member policies and restriction locks. Stewardship is
    /// verified against the database inside the orchestration layer (bearer
    /// claims are stale by design). 403s carry machine-readable reasons.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/household/{householdId:long}")]
    public class HouseholdPolicyController : BaseApiController
    {
        private readonly IHouseholdPolicyOrchestrationService _policyService;

        public HouseholdPolicyController(IHouseholdPolicyOrchestrationService policyService)
        {
            _policyService = policyService;
        }

        [HttpGet("members/{personId:long}/policy")]
        public async Task<ActionResult<MemberPolicyModel>> GetMemberPolicy(long householdId, long personId)
        {
            var requester = GetCurrentPersonId();
            if (!requester.HasValue) return Unauthorized();

            try
            {
                return Ok(await _policyService.GetMemberPolicyAsync(householdId, personId, requester.Value));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message, reason = "steward_required" });
            }
        }

        [HttpPut("members/{personId:long}/policy")]
        public async Task<ActionResult<MemberPolicyModel>> SetMemberPolicy(long householdId, long personId, [FromBody] MemberPolicyModel model)
        {
            var requester = GetCurrentPersonId();
            if (!requester.HasValue) return Unauthorized();

            model.HouseholdId = householdId;
            model.PersonId = personId;
            try
            {
                return Ok(await _policyService.SetMemberPolicyAsync(model, requester.Value));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message, reason = "steward_required" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("restrictions/{restrictionId:long}/lock")]
        public Task<IActionResult> Lock(long householdId, long restrictionId) =>
            SetLockAsync(householdId, restrictionId, locked: true);

        [HttpDelete("restrictions/{restrictionId:long}/lock")]
        public Task<IActionResult> Unlock(long householdId, long restrictionId) =>
            SetLockAsync(householdId, restrictionId, locked: false);

        [HttpPost("members/{personId:long}/restrictions")]
        public async Task<IActionResult> AddMemberRestriction(long householdId, long personId, [FromBody] StewardRestrictionRequestModel request)
        {
            var requester = GetCurrentPersonId();
            if (!requester.HasValue) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Restriction name is required." });
            }

            try
            {
                var id = await _policyService.AddMemberRestrictionAsync(householdId, personId, request, requester.Value);
                return Created($"api/person/{personId}/restrictions", new { id });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message, reason = "steward_required" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<IActionResult> SetLockAsync(long householdId, long restrictionId, bool locked)
        {
            var requester = GetCurrentPersonId();
            if (!requester.HasValue) return Unauthorized();

            try
            {
                var ok = await _policyService.SetRestrictionLockAsync(householdId, restrictionId, locked, requester.Value);
                return ok ? NoContent() : NotFound();
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message, reason = "steward_required" });
            }
        }
    }
}
