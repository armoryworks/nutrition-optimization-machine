using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Nom.Orch.Interfaces;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Audience management for audience-scoped recipe visibility. Owner-only;
    /// audiences maintained by an external manager are read-only here.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/audience")]
    public class AudienceController : BaseApiController
    {
        public sealed record CreateAudienceRequest(string Name);

        private readonly IAudienceOrchestrationService _audienceService;

        public AudienceController(IAudienceOrchestrationService audienceService)
        {
            _audienceService = audienceService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMine()
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue) return Unauthorized();
            return Ok(await _audienceService.GetMineAsync(personId.Value));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAudienceRequest request)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Audience name is required." });
            }
            var audience = await _audienceService.CreateAsync(request.Name, personId.Value);
            return CreatedAtAction(nameof(GetMine), null, audience);
        }

        [HttpDelete("{id:long}")]
        public Task<IActionResult> Delete(long id) =>
            GuardedAsync(personId => _audienceService.DeleteAsync(id, personId));

        [HttpPost("{id:long}/households/{householdId:long}")]
        public Task<IActionResult> AddHousehold(long id, long householdId) =>
            GuardedAsync(personId => _audienceService.AddHouseholdAsync(id, householdId, personId));

        [HttpDelete("{id:long}/households/{householdId:long}")]
        public Task<IActionResult> RemoveHousehold(long id, long householdId) =>
            GuardedAsync(personId => _audienceService.RemoveHouseholdAsync(id, householdId, personId));

        [HttpPost("{id:long}/recipes/{recipeId:long}")]
        public Task<IActionResult> AttachRecipe(long id, long recipeId) =>
            GuardedAsync(personId => _audienceService.AttachRecipeAsync(id, recipeId, personId));

        [HttpDelete("{id:long}/recipes/{recipeId:long}")]
        public Task<IActionResult> DetachRecipe(long id, long recipeId) =>
            GuardedAsync(personId => _audienceService.DetachRecipeAsync(id, recipeId, personId));

        private async Task<IActionResult> GuardedAsync(Func<long, Task<bool>> action)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue) return Unauthorized();

            try
            {
                var ok = await action(personId.Value);
                return ok ? NoContent() : NotFound();
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message, reason = "audience_owner_required" });
            }
        }
    }
}
