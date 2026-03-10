using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InvitationController : BaseApiController
    {
        private readonly IInvitationOrchestrationService _invitationOrchestrationService;

        public InvitationController(IInvitationOrchestrationService invitationOrchestrationService)
        {
            _invitationOrchestrationService = invitationOrchestrationService;
        }

        [HttpPost]
        public async Task<ActionResult<InvitationModel>> CreateInvitation([FromBody] CreateInvitationRequest request)
        {
            var inviterPersonId = GetCurrentPersonIdRequired();
            var invitation = await _invitationOrchestrationService.CreateInvitationAsync(request, inviterPersonId);
            return CreatedAtAction(nameof(GetInvitationByCode), new { code = invitation.Code }, invitation);
        }

        [HttpPost("claim")]
        public async Task<ActionResult<InvitationModel>> ClaimInvitation([FromBody] ClaimInvitationRequest request)
        {
            var invitation = await _invitationOrchestrationService.ClaimInvitationAsync(request);
            return Ok(invitation);
        }

        [HttpGet("validate/{code}")]
        public async Task<ActionResult<bool>> ValidateInvitation(string code)
        {
            var isValid = await _invitationOrchestrationService.ValidateInvitationAsync(code);
            return Ok(isValid);
        }

        [HttpGet("code/{code}")]
        public async Task<ActionResult<InvitationModel>> GetInvitationByCode(string code)
        {
            var invitation = await _invitationOrchestrationService.GetInvitationByCodeAsync(code);
            if (invitation == null)
                return NotFound();

            return Ok(invitation);
        }

        [HttpGet("inviter/{inviterPersonId}")]
        public async Task<ActionResult<List<InvitationModel>>> GetInvitationsByInviter(long inviterPersonId)
        {
            var invitations = await _invitationOrchestrationService.GetInvitationsByInviterAsync(inviterPersonId);
            return Ok(invitations);
        }

        [HttpGet("invitee/{inviteePersonId}")]
        public async Task<ActionResult<List<InvitationModel>>> GetInvitationsByInvitee(long inviteePersonId)
        {
            var invitations = await _invitationOrchestrationService.GetInvitationsByInviteeAsync(inviteePersonId);
            return Ok(invitations);
        }
    }
}