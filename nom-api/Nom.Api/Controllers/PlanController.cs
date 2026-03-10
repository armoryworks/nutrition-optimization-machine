using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class PlanController : BaseApiController
    {
        private readonly IPlanOrchestrationService _planOrch;

        public PlanController(IPlanOrchestrationService planOrch)
        {
            _planOrch = planOrch;
        }

        [HttpGet("curated")]
        public async Task<IActionResult> GetCuratedPlans()
        {
            var plans = await _planOrch.GetCuratedPlansAsync();
            return Ok(plans);
        }

        [HttpGet("my-plans")]
        public async Task<IActionResult> GetMyPlans()
        {
            var authorId = GetCurrentPersonIdRequired();
            var plans = await _planOrch.GetMyPlansAsync(authorId);
            return Ok(plans);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetPlanById(long id)
        {
            var plan = await _planOrch.GetPlanByIdAsync(id);
            return Ok(plan);
        }

        [HttpPost("clone")]
        public async Task<IActionResult> ClonePlan([FromBody] ClonePlanRequest request)
        {
            var newAuthorId = GetCurrentPersonIdRequired();
            var clonedPlan = await _planOrch.ClonePlanAsync(request.SourcePlanId, newAuthorId, request.NewPlanName);
            return Ok(clonedPlan);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request)
        {
            var authorId = GetCurrentPersonIdRequired();
            var plan = await _planOrch.CreatePlanAsync(request, authorId);
            return CreatedAtAction(nameof(GetPlanById), new { id = plan.Id }, plan);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdatePlan(long id, [FromBody] UpdatePlanRequest request)
        {
            var newAuthorId = GetCurrentPersonIdRequired();
            await _planOrch.UpdatePlanAsync(id, request, newAuthorId);
            return Ok();
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> DeletePlan(long id)
        {
            var authorId = GetCurrentPersonIdRequired();
            await _planOrch.DeletePlanAsync(id, authorId);
            return Ok();
        }

        [HttpPost("{id:long}/submit-for-curation")]
        public async Task<IActionResult> SubmitPlanForCuration(long id)
        {
            var authorId = GetCurrentPersonIdRequired();
            await _planOrch.SubmitPlanForCurationAsync(id, authorId);
            return Ok();
        }
    }
}