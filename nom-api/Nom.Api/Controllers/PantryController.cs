using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Pantry;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PantryController : BaseApiController
    {
        private readonly IPantryOrchestrationService _pantryService;
        private readonly ILogger<PantryController> _logger;

        public PantryController(
            IPantryOrchestrationService pantryService,
            ILogger<PantryController> logger)
        {
            _pantryService = pantryService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<PantryItemResponseModel>>> GetPantryItems(
            [FromQuery] long householdId)
        {
            try
            {
                var items = await _pantryService.GetPantryItemsAsync(householdId);
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pantry items for household {HouseholdId}", householdId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to retrieve pantry items" });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PantryItemResponseModel>> GetPantryItem(long id)
        {
            try
            {
                var item = await _pantryService.GetPantryItemAsync(id);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pantry item {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to retrieve pantry item" });
            }
        }

        [HttpPost]
        public async Task<ActionResult<PantryItemResponseModel>> AddPantryItem(
            [FromBody] PantryItemCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var item = await _pantryService.AddPantryItemAsync(model);
                return CreatedAtAction(nameof(GetPantryItem), new { id = item.Id }, item);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding pantry item");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to add pantry item" });
            }
        }

        [HttpPost("batch")]
        public async Task<ActionResult<List<PantryItemResponseModel>>> AddPantryItemsBatch(
            [FromBody] List<PantryItemCreateModel> items)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (items == null || items.Count == 0)
                return BadRequest(new { message = "No items provided" });

            try
            {
                var created = await _pantryService.AddPantryItemsBatchAsync(items);
                return Ok(created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding pantry items batch ({Count} items)", items.Count);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to add pantry items" });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<PantryItemResponseModel>> UpdatePantryItem(
            long id, [FromBody] PantryItemUpdateModel model)
        {
            try
            {
                var item = await _pantryService.UpdatePantryItemAsync(id, model);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating pantry item {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to update pantry item" });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> RemovePantryItem(long id)
        {
            try
            {
                var result = await _pantryService.RemovePantryItemAsync(id);
                if (!result) return NotFound();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing pantry item {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to remove pantry item" });
            }
        }

        [HttpGet("shopping-needs")]
        public async Task<ActionResult<ShoppingNeedsResponseModel>> GetShoppingNeeds(
            [FromQuery] long householdId,
            [FromQuery] int daysAhead = 4)
        {
            try
            {
                var needs = await _pantryService.GetShoppingNeedsAsync(householdId, daysAhead);
                return Ok(needs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing shopping needs for household {HouseholdId}", householdId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to compute shopping needs" });
            }
        }
    }
}
