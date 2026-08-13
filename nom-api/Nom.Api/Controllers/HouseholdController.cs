using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Household;
using Nom.Orch.Models.Plan;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HouseholdController : BaseApiController
    {
        private readonly IHouseholdOrchestrationService _householdService;
        private readonly IMacroGoalOrchestrationService _macroGoalService;

        public HouseholdController(
            IHouseholdOrchestrationService householdService,
            IMacroGoalOrchestrationService macroGoalService)
        {
            _householdService = householdService;
            _macroGoalService = macroGoalService;
        }

        /// <summary>
        /// Gets the household's default daily macro goals. Null targets mean unset.
        /// </summary>
        [HttpGet("{id:long}/macro-goals")]
        public async Task<ActionResult<MacroGoalModel>> GetMacroGoals(long id)
        {
            if (!IsHouseholdMember(id))
                return Forbid();

            var goal = await _macroGoalService.GetHouseholdGoalAsync(id);
            return Ok(goal ?? new MacroGoalModel());
        }

        /// <summary>
        /// Creates or replaces the household's default daily macro goals.
        /// These apply to members without a personal goal and steer meal-plan
        /// shuffle selection for the household.
        /// </summary>
        [HttpPut("{id:long}/macro-goals")]
        public async Task<ActionResult<MacroGoalModel>> SaveMacroGoals(long id, [FromBody] MacroGoalModel request)
        {
            if (!CanManageHousehold(id))
                return Forbid();

            var saved = await _macroGoalService.SaveHouseholdGoalAsync(id, request);
            return Ok(saved);
        }

        [HttpGet]
        public async Task<ActionResult<List<HouseholdResponseModel>>> GetHouseholds()
        {
            var householdIds = GetUserHouseholdIds();
            var households = await _householdService.GetHouseholdsForMemberAsync(householdIds);
            return Ok(households);
        }

        [HttpPost]
        public async Task<ActionResult<HouseholdCreateResponseModel>> CreateHousehold([FromBody] HouseholdCreateModel request)
        {
            var personId = GetCurrentPersonId();
            var response = await _householdService.CreateHouseholdAsync(request, personId);
            return CreatedAtAction(nameof(GetHousehold), new { id = response.Id }, response);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<HouseholdResponseModel>> GetHousehold(long id)
        {
            if (!IsHouseholdMember(id))
                return Forbid();

            var household = await _householdService.GetHouseholdAsync(id);
            if (household == null)
            {
                return NotFound(new { message = "Household not found" });
            }
            return Ok(household);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<HouseholdResponseModel>> UpdateHousehold(long id, [FromBody] HouseholdUpdateModel request)
        {
            if (!CanManageHousehold(id))
                return Forbid();

            var response = await _householdService.UpdateHouseholdAsync(id, request);
            if (response == null)
            {
                return NotFound(new { message = "Household not found" });
            }
            return Ok(response);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteHousehold(long id)
        {
            if (!IsHouseholdAdmin(id))
                return Forbid();

            var success = await _householdService.DeleteHouseholdAsync(id);
            if (!success)
            {
                return NotFound(new { message = "Household not found" });
            }
            return NoContent();
        }

        [HttpPost("invite-token")]
        public async Task<ActionResult<HouseholdInviteTokenResponseModel>> CreateInviteToken([FromBody] HouseholdInviteTokenCreateModel request)
        {
            if (!CanInviteToHousehold(request.HouseholdId))
                return Forbid();

            var response = await _householdService.CreateInviteTokenAsync(request);
            return Ok(response);
        }

        [HttpPost("member")]
        public async Task<ActionResult<HouseholdMemberResponseModel>> AddMember([FromBody] HouseholdMemberCreateModel request)
        {
            if (!CanManageHousehold(request.HouseholdId))
                return Forbid();

            var response = await _householdService.AddMemberAsync(request);
            return Ok(response);
        }

        [HttpDelete("{householdId}/member/{memberId}")]
        public async Task<ActionResult> RemoveMember(long householdId, long memberId)
        {
            if (!CanManageHousehold(householdId))
                return Forbid();

            var success = await _householdService.RemoveMemberAsync(householdId, memberId);
            if (!success)
            {
                return NotFound(new { message = "Member not found in household" });
            }
            return NoContent();
        }

        [HttpPost("join")]
        public async Task<ActionResult<HouseholdMemberResponseModel>> JoinHousehold([FromBody] JoinHouseholdRequestModel request)
        {
            // Get the current authenticated user's person ID from claims
            var personId = GetCurrentPersonIdRequired();

            var response = await _householdService.JoinHouseholdAsync(request.Token, personId);
            return Ok(response);
        }
    }
}
