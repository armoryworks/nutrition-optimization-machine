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
        private readonly ILogger<RetailPackagingController> _logger;
        private readonly IRetailPackagingOrchestrationService _retailPackagingOrch;

        public RetailPackagingController(
            ILogger<RetailPackagingController> logger,
            IRetailPackagingOrchestrationService retailPackagingOrch)
        {
            _logger = logger;
            _retailPackagingOrch = retailPackagingOrch;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var items = await _retailPackagingOrch.GetAllAsync();
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving retail packaging data");
                return StatusCode(500, "An unexpected error occurred.");
            }
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            try
            {
                var item = await _retailPackagingOrch.GetByIdAsync(id);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving retail packaging {Id}", id);
                return StatusCode(500, "An unexpected error occurred.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RetailPackagingCreateModel model)
        {
            try
            {
                var item = await _retailPackagingOrch.CreateAsync(model);
                return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating retail packaging");
                return StatusCode(500, "An unexpected error occurred.");
            }
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] RetailPackagingUpdateModel model)
        {
            try
            {
                var item = await _retailPackagingOrch.UpdateAsync(id, model);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating retail packaging {Id}", id);
                return StatusCode(500, "An unexpected error occurred.");
            }
        }

        [HttpPost("lookup")]
        public async Task<IActionResult> Lookup(
            [FromBody] RetailPackagingLookupRequest request,
            CancellationToken ct)
        {
            try
            {
                if (request.IngredientNames == null || request.IngredientNames.Count == 0)
                    return BadRequest("At least one ingredient name is required.");
                if (request.IngredientNames.Count > 50)
                    return BadRequest("Maximum 50 ingredient names per request.");

                var result = await _retailPackagingOrch.LookupPackagingAsync(request.IngredientNames, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error looking up retail packaging");
                return StatusCode(500, "An unexpected error occurred.");
            }
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            try
            {
                var deleted = await _retailPackagingOrch.DeleteAsync(id);
                if (!deleted) return NotFound();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting retail packaging {Id}", id);
                return StatusCode(500, "An unexpected error occurred.");
            }
        }
    }
}
