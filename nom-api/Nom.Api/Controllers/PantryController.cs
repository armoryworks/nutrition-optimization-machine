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
            if (!IsHouseholdMember(householdId))
                return Forbid();

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

                if (!IsHouseholdMember(item.HouseholdId))
                    return Forbid();

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

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

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

            // Verify membership for all household IDs in the batch
            var householdIds = items.Select(i => i.HouseholdId).Distinct().ToList();
            if (householdIds.Any(hId => !IsHouseholdMember(hId)))
                return Forbid();

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
                var existing = await _pantryService.GetPantryItemAsync(id);
                if (existing == null) return NotFound();

                if (!IsHouseholdMember(existing.HouseholdId))
                    return Forbid();

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
                var existing = await _pantryService.GetPantryItemAsync(id);
                if (existing == null) return NotFound();

                if (!IsHouseholdMember(existing.HouseholdId))
                    return Forbid();

                await _pantryService.RemovePantryItemAsync(id);
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
            if (!IsHouseholdMember(householdId))
                return Forbid();

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
