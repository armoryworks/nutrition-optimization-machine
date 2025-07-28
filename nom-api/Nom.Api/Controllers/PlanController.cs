using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class PlanController : BaseApiController
    {
        private readonly ILogger<PlanController> _logger;
        private readonly IPlanOrchestrationService _planOrch;

        public PlanController(ILogger<PlanController> logger, IPlanOrchestrationService planOrch)
        {
            _logger = logger;
            _planOrch = planOrch;
        }

        [HttpGet("curated")]
        public async Task<IActionResult> GetCuratedPlans()
        {
            try
            {
                var plans = await _planOrch.GetCuratedPlansAsync();
                return Ok(plans);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving curated plans.");
                return StatusCode(500, "An unexpected error occurred while retrieving curated plans.");
            }
        }

        [HttpGet("my-plans")]
        public async Task<IActionResult> GetMyPlans()
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var plans = await _planOrch.GetMyPlansAsync(authorId);
                return Ok(plans);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user plans.");
                return StatusCode(500, "An unexpected error occurred while retrieving your plans.");
            }
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetPlanById(long id)
        {
            try
            {
                var plan = await _planOrch.GetPlanByIdAsync(id);
                return Ok(plan);
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving plan {PlanId}.", id);
                return StatusCode(500, "An unexpected error occurred while retrieving the plan.");
            }
        }

        [HttpPost("clone")]
        public async Task<IActionResult> ClonePlan([FromBody] ClonePlanRequest request)
        {
            try
            {
                var newAuthorId = GetCurrentPersonId();
                var clonedPlan = await _planOrch.ClonePlanAsync(request.SourcePlanId, newAuthorId, request.NewPlanName);
                return Ok(clonedPlan);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cloning plan {SourcePlanId}.", request.SourcePlanId);
                return StatusCode(500, "An unexpected error occurred while cloning the plan.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var plan = await _planOrch.CreatePlanAsync(request, authorId);
                return CreatedAtAction(nameof(GetPlanById), new { id = plan.Id }, plan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating plan.");
                return StatusCode(500, "An unexpected error occurred while creating the plan.");
            }
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdatePlan(long id, [FromBody] UpdatePlanRequest request)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                await _planOrch.UpdatePlanAsync(id, request, authorId);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating plan {PlanId}.", id);
                return StatusCode(500, "An unexpected error occurred while updating the plan.");
            }
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> DeletePlan(long id)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                await _planOrch.DeletePlanAsync(id, authorId);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting plan {PlanId}.", id);
                return StatusCode(500, "An unexpected error occurred while deleting the plan.");
            }
        }

        [HttpPost("{id:long}/submit-for-curation")]
        public async Task<IActionResult> SubmitPlanForCuration(long id)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                await _planOrch.SubmitPlanForCurationAsync(id, authorId);
                return Ok();
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting plan {PlanId} for curation.", id);
                return StatusCode(500, "An unexpected error occurred while submitting the plan for curation.");
            }
        }
    }

    public class ClonePlanRequest
    {
        public long SourcePlanId { get; set; }
        public string NewPlanName { get; set; } = string.Empty;
    }
} 