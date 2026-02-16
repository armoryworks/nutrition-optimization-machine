// File: Nom.Api/Controllers/CurationController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class CurationController : BaseApiController
    {
        private readonly ILogger<CurationController> _logger;
        private readonly ICurationOrchestrationService _curationOrch;

        public CurationController(ILogger<CurationController> logger, ICurationOrchestrationService curationOrch)
        {
            _logger = logger;
            _curationOrch = curationOrch;
        }

        [HttpGet("queue")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> GetCurationQueue()
        {
            try
            {
                var queueItems = await _curationOrch.GetCurationQueueAsync();
                return Ok(queueItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving curation queue.");
                return StatusCode(500, "An unexpected error occurred while retrieving the curation queue.");
            }
        }

        [HttpPost("submit")]
        // Any authenticated user can submit their own content for curation
        public async Task<IActionResult> SubmitForCuration([FromBody] SubmitForCurationRequest request)
        {
            try
            {
                var authorPersonId = GetCurrentPersonIdRequired();
                await _curationOrch.SubmitForCurationAsync(request, authorPersonId);
                return Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete. Please complete registration first.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting {EntityType} {EntityId} for curation.", request.EntityType, request.EntityId);
                return StatusCode(500, "An unexpected error occurred during submission.");
            }
        }

        [HttpPost("approve")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> Approve([FromBody] CurationDecisionRequest request)
        {
            try
            {
                var adminPersonId = GetCurrentPersonIdRequired();
                await _curationOrch.ApproveAsync(request, adminPersonId);
                return Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete. Please complete registration first.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving {EntityType} {EntityId}.", request.EntityType, request.EntityId);
                return StatusCode(500, "An unexpected error occurred during approval.");
            }
        }

        [HttpPost("request-revision")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> RequestRevision([FromBody] CurationDecisionRequest request)
        {
            try
            {
                var adminPersonId = GetCurrentPersonIdRequired();
                await _curationOrch.RequestRevisionAsync(request, adminPersonId);
                return Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete. Please complete registration first.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting revision for {EntityType} {EntityId}.", request.EntityType, request.EntityId);
                return StatusCode(500, "An unexpected error occurred while requesting revision.");
            }
        }

        [HttpPost("reject")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> Reject([FromBody] CurationDecisionRequest request)
        {
            try
            {
                var adminPersonId = GetCurrentPersonIdRequired();
                await _curationOrch.RejectAsync(request, adminPersonId);
                return Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete. Please complete registration first.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting {EntityType} {EntityId}.", request.EntityType, request.EntityId);
                return StatusCode(500, "An unexpected error occurred during rejection.");
            }
        }
    }
}