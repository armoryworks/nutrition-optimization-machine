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

        public PantryController(
            IPantryOrchestrationService pantryService)
        {
            _pantryService = pantryService;
        }

        [HttpGet]
        public async Task<ActionResult<List<PantryItemResponseModel>>> GetPantryItems(
            [FromQuery] long householdId)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var items = await _pantryService.GetPantryItemsAsync(householdId);
            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PantryItemResponseModel>> GetPantryItem(long id)
        {
            var item = await _pantryService.GetPantryItemAsync(id);
            if (item == null) return NotFound();

            if (!IsHouseholdMember(item.HouseholdId))
                return Forbid();

            return Ok(item);
        }

        [HttpPost]
        public async Task<ActionResult<PantryItemResponseModel>> AddPantryItem(
            [FromBody] PantryItemCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

            var item = await _pantryService.AddPantryItemAsync(model);
            return CreatedAtAction(nameof(GetPantryItem), new { id = item.Id }, item);
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

            var created = await _pantryService.AddPantryItemsBatchAsync(items);
            return Created("api/pantry", created);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<PantryItemResponseModel>> UpdatePantryItem(
            long id, [FromBody] PantryItemUpdateModel model)
        {
            var existing = await _pantryService.GetPantryItemAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var item = await _pantryService.UpdatePantryItemAsync(id, model);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> RemovePantryItem(long id)
        {
            var existing = await _pantryService.GetPantryItemAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            await _pantryService.RemovePantryItemAsync(id);
            return NoContent();
        }

        [HttpGet("shopping-needs")]
        public async Task<ActionResult<ShoppingNeedsResponseModel>> GetShoppingNeeds(
            [FromQuery] long householdId,
            [FromQuery] int daysAhead = 4)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var needs = await _pantryService.GetShoppingNeedsAsync(householdId, daysAhead);
            return Ok(needs);
        }
    }
}
