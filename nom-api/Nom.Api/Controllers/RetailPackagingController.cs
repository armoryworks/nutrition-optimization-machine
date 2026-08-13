using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RetailPackagingController : BaseApiController
    {
        private readonly IRetailPackagingOrchestrationService _retailPackagingOrch;

        public RetailPackagingController(
            IRetailPackagingOrchestrationService retailPackagingOrch)
        {
            _retailPackagingOrch = retailPackagingOrch;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _retailPackagingOrch.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var item = await _retailPackagingOrch.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [Authorize(Policy = "CanManageCuration")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RetailPackagingCreateModel model)
        {
            var item = await _retailPackagingOrch.CreateAsync(model);
            return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
        }

        [Authorize(Policy = "CanManageCuration")]
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] RetailPackagingUpdateModel model)
        {
            var item = await _retailPackagingOrch.UpdateAsync(id, model);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost("lookup")]
        public async Task<IActionResult> Lookup(
            [FromBody] RetailPackagingLookupRequest request,
            CancellationToken ct)
        {
            if (request.IngredientNames == null || request.IngredientNames.Count == 0)
                return BadRequest("At least one ingredient name is required.");
            if (request.IngredientNames.Count > 50)
                return BadRequest("Maximum 50 ingredient names per request.");

            var result = await _retailPackagingOrch.LookupPackagingAsync(request.IngredientNames, ct);
            return Ok(result);
        }

        [Authorize(Policy = "CanManageCuration")]
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var deleted = await _retailPackagingOrch.DeleteAsync(id);
            if (!deleted) return NotFound();
            return NoContent();
        }
    }
}
