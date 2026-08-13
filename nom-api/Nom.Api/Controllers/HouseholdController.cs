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
        private readonly IPortionOrchestrationService _portionService;

        public HouseholdController(
            IHouseholdOrchestrationService householdService,
            IMacroGoalOrchestrationService macroGoalService,
            IPortionOrchestrationService portionService)
        {
            _householdService = householdService;
            _macroGoalService = macroGoalService;
            _portionService = portionService;
        }

        /// <summary>
        /// 400 body when a membership-growth action hits a personal kitchen —
        /// the client should run the convert-to-shared flow first.
        /// </summary>
        private ObjectResult PersonalHousehold() =>
            BadRequest(new
            {
                message = "This is a personal kitchen. Convert it into a shared household before inviting or adding members.",
                reason = "personal_household"
            });

        /// <summary>
        /// Gets the household's meal-split percentages (daily calorie budget by
        /// meal type). Returns defaults (25/30/35/10) when unset.
        /// </summary>
        [HttpGet("{id:long}/meal-split")]
        public async Task<ActionResult<MealSplitModel>> GetMealSplit(long id)
        {
            if (!IsHouseholdMember(id))
                return Forbid();

            return Ok(await _portionService.GetMealSplitAsync(id));
        }

        /// <summary>
        /// Creates or replaces the household's meal-split percentages.
        /// Percentages must sum to 100.
        /// </summary>
        [HttpPut("{id:long}/meal-split")]
        public async Task<ActionResult<MealSplitModel>> SaveMealSplit(long id, [FromBody] MealSplitModel request)
        {
            if (!CanManageHousehold(id))
                return Forbid();

            try
            {
                return Ok(await _portionService.SaveMealSplitAsync(id, request));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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
            // IsPersonal is server-controlled: personal kitchens are created
            // only via POST household/personal (or the onboarding solo path).
            request.IsPersonal = false;

            var personId = GetCurrentPersonId();
            var response = await _householdService.CreateHouseholdAsync(request, personId);
            return CreatedAtAction(nameof(GetHousehold), new { id = response.Id }, response);
        }

        /// <summary>
        /// "Just cooking for myself": creates the caller's personal kitchen —
        /// name and IsPersonal flag are decided server-side.
        /// </summary>
        [HttpPost("personal")]
        public async Task<ActionResult<HouseholdCreateResponseModel>> CreatePersonalHousehold()
        {
            var personId = GetCurrentPersonIdRequired();

            try
            {
                var response = await _householdService.CreatePersonalHouseholdAsync(personId);
                return CreatedAtAction(nameof(GetHousehold), new { id = response.Id }, response);
            }
            catch (InvalidOperationException ex) when (ex.Message == "already_in_household")
            {
                return BadRequest(new { message = "You already belong to a household.", reason = "already_in_household" });
            }
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

        /// <summary>
        /// External-management enrollment info for the household (design doc §5):
        /// the consent UI checks managedBy before offering per-adult consent.
        /// </summary>
        [HttpGet("{id:long}/enrollment-info")]
        public async Task<ActionResult<HouseholdEnrollmentInfoModel>> GetEnrollmentInfo(long id)
        {
            if (!IsHouseholdMember(id))
                return Forbid();

            var info = await _householdService.GetEnrollmentInfoAsync(id);
            if (info == null)
            {
                return NotFound(new { message = "Household not found" });
            }
            return Ok(info);
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

            try
            {
                var response = await _householdService.CreateInviteTokenAsync(request);
                return Ok(response);
            }
            catch (InvalidOperationException ex) when (ex.Message == "personal_household")
            {
                return PersonalHousehold();
            }
        }

        [HttpPost("member")]
        public async Task<ActionResult<HouseholdMemberResponseModel>> AddMember([FromBody] HouseholdMemberCreateModel request)
        {
            if (!CanManageHousehold(request.HouseholdId))
                return Forbid();

            try
            {
                var response = await _householdService.AddMemberAsync(request);
                return Ok(response);
            }
            catch (InvalidOperationException ex) when (ex.Message == "personal_household")
            {
                return PersonalHousehold();
            }
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

            try
            {
                var response = await _householdService.JoinHouseholdAsync(request.Token, personId);
                return Ok(response);
            }
            catch (InvalidOperationException ex) when (ex.Message == "personal_household")
            {
                return PersonalHousehold();
            }
        }

        /// <summary>
        /// Converts a personal kitchen into a shared household (invoked by the
        /// first-invite interstitial): renames it and clears the personal flag.
        /// Steward-only; 400 when the household is not personal.
        /// </summary>
        [HttpPost("{id:long}/convert")]
        public async Task<ActionResult<HouseholdResponseModel>> ConvertToShared(long id, [FromBody] HouseholdConvertModel request)
        {
            var personId = GetCurrentPersonIdRequired();

            try
            {
                var response = await _householdService.ConvertToSharedAsync(id, request.Name, personId);
                if (response == null)
                {
                    return NotFound(new { message = "Household not found" });
                }
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message, reason = "steward_required" });
            }
            catch (InvalidOperationException ex) when (ex.Message == "not_personal")
            {
                return BadRequest(new { message = "This household is already shared.", reason = "not_personal" });
            }
        }
    }
}
