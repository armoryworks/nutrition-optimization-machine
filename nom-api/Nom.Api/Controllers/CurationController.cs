// File: Nom.Api/Controllers/CurationController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class CurationController : BaseApiController
    {
        private readonly ICurationOrchestrationService _curationOrch;

        public CurationController(ICurationOrchestrationService curationOrch)
        {
            _curationOrch = curationOrch;
        }

        [HttpGet("queue")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> GetCurationQueue()
        {
            var queueItems = await _curationOrch.GetCurationQueueAsync();
            return Ok(queueItems);
        }

        [HttpPost("submit")]
        // Any authenticated user can submit their own content for curation
        public async Task<IActionResult> SubmitForCuration([FromBody] SubmitForCurationRequest request)
        {
            var authorPersonId = GetCurrentPersonIdRequired();
            await _curationOrch.SubmitForCurationAsync(request, authorPersonId);
            return Ok();
        }

        [HttpPost("approve")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> Approve([FromBody] CurationDecisionRequest request)
        {
            var adminPersonId = GetCurrentPersonIdRequired();
            await _curationOrch.ApproveAsync(request, adminPersonId);
            return Ok();
        }

        [HttpPost("request-revision")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> RequestRevision([FromBody] CurationDecisionRequest request)
        {
            var adminPersonId = GetCurrentPersonIdRequired();
            await _curationOrch.RequestRevisionAsync(request, adminPersonId);
            return Ok();
        }

        [HttpPost("reject")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> Reject([FromBody] CurationDecisionRequest request)
        {
            var adminPersonId = GetCurrentPersonIdRequired();
            await _curationOrch.RejectAsync(request, adminPersonId);
            return Ok();
        }
    }
}