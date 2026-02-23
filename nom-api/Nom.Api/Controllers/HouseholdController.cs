using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Household;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HouseholdController : BaseApiController
    {
        private readonly IHouseholdOrchestrationService _householdService;

        public HouseholdController(IHouseholdOrchestrationService householdService)
        {
            _householdService = householdService;
        }

        [HttpGet]
        public async Task<ActionResult<List<HouseholdResponseModel>>> GetHouseholds()
        {
            try
            {
                var householdIds = GetUserHouseholdIds();
                var households = await _householdService.GetHouseholdsForMemberAsync(householdIds);
                return Ok(households);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve households", error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<HouseholdCreateResponseModel>> CreateHousehold([FromBody] HouseholdCreateModel request)
        {
            try
            {
                var personId = GetCurrentPersonId();
                var response = await _householdService.CreateHouseholdAsync(request, personId);
                return CreatedAtAction(nameof(GetHousehold), new { id = response.Id }, response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create household", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<HouseholdResponseModel>> GetHousehold(long id)
        {
            if (!IsHouseholdMember(id))
                return Forbid();

            try
            {
                var household = await _householdService.GetHouseholdAsync(id);
                if (household == null)
                {
                    return NotFound(new { message = "Household not found" });
                }
                return Ok(household);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve household", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<HouseholdResponseModel>> UpdateHousehold(long id, [FromBody] HouseholdUpdateModel request)
        {
            if (!CanManageHousehold(id))
                return Forbid();

            try
            {
                var response = await _householdService.UpdateHouseholdAsync(id, request);
                if (response == null)
                {
                    return NotFound(new { message = "Household not found" });
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update household", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteHousehold(long id)
        {
            if (!IsHouseholdAdmin(id))
                return Forbid();

            try
            {
                var success = await _householdService.DeleteHouseholdAsync(id);
                if (!success)
                {
                    return NotFound(new { message = "Household not found" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete household", error = ex.Message });
            }
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
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create invite token", error = ex.Message });
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
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to add member", error = ex.Message });
            }
        }

        [HttpDelete("{householdId}/member/{memberId}")]
        public async Task<ActionResult> RemoveMember(long householdId, long memberId)
        {
            if (!CanManageHousehold(householdId))
                return Forbid();

            try
            {
                var success = await _householdService.RemoveMemberAsync(householdId, memberId);
                if (!success)
                {
                    return NotFound(new { message = "Member not found in household" });
                }
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to remove member", error = ex.Message });
            }
        }

        [HttpPost("join")]
        public async Task<ActionResult<HouseholdMemberResponseModel>> JoinHousehold([FromBody] JoinHouseholdRequestModel request)
        {
            try
            {
                // Get the current authenticated user's person ID from claims
                var personId = GetCurrentPersonIdRequired();

                var response = await _householdService.JoinHouseholdAsync(request.Token, personId);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to join household", error = ex.Message });
            }
        }
    }
}
