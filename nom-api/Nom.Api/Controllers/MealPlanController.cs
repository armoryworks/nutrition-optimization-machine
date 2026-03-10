using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Models.Pantry;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MealPlanController : BaseApiController
    {
        private readonly IMealPlanOrchestrationService _mealPlanOrchestrationService;
        private readonly IPantryOrchestrationService _pantryService;

        public MealPlanController(
            IMealPlanOrchestrationService mealPlanOrchestrationService,
            IPantryOrchestrationService pantryService)
        {
            _mealPlanOrchestrationService = mealPlanOrchestrationService;
            _pantryService = pantryService;
        }

        [HttpGet]
        public async Task<ActionResult<List<MealPlanResponseModel>>> GetMealPlans(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            var householdIds = GetUserHouseholdIds();
            var mealPlans = await _mealPlanOrchestrationService.GetAllMealPlansAsync(startDate, endDate, householdIds);
            return Ok(mealPlans);
        }

        [HttpPost]
        [ProducesResponseType(typeof(MealPlanCreateResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateMealPlan([FromBody] MealPlanCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

            var authorId = GetCurrentPersonIdRequired();
            var response = await _mealPlanOrchestrationService.CreateMealPlanAsync(model, authorId);
            return CreatedAtAction(nameof(GetMealPlan), new { id = response.Id }, response);
        }

        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(MealPlanResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMealPlan([Required] long id)
        {
            var response = await _mealPlanOrchestrationService.GetMealPlanAsync(id);
            if (response == null)
                return NotFound();

            if (!IsHouseholdMember(response.HouseholdId))
                return Forbid();

            return Ok(response);
        }

        [HttpPut("{id:long}")]
        [ProducesResponseType(typeof(MealPlanResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateMealPlan([Required] long id, [FromBody] MealPlanUpdateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var existing = await _mealPlanOrchestrationService.GetMealPlanAsync(id);
            if (existing == null)
                return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var response = await _mealPlanOrchestrationService.UpdateMealPlanAsync(id, model);
            return Ok(response);
        }

        [HttpDelete("{id:long}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteMealPlan([Required] long id)
        {
            var existing = await _mealPlanOrchestrationService.GetMealPlanAsync(id);
            if (existing == null)
                return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            await _mealPlanOrchestrationService.DeleteMealPlanAsync(id);
            return Ok(new { Message = "Meal plan deleted successfully." });
        }

        [HttpPost("shuffle")]
        [ProducesResponseType(typeof(MealPlanShuffleResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ShuffleMealPlans([FromBody] MealPlanShuffleModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

            var authorId = GetCurrentPersonIdRequired();
            var response = await _mealPlanOrchestrationService.ShuffleMealPlansAsync(model, authorId);
            return Ok(response);
        }

        [HttpPost("rule")]
        [ProducesResponseType(typeof(MealPlanRuleCreateResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateRule([FromBody] MealPlanRuleCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

            var response = await _mealPlanOrchestrationService.CreateRuleAsync(model);
            return CreatedAtAction(nameof(GetRule), new { id = response.Id }, response);
        }

        [HttpGet("rule/{id}")]
        [ProducesResponseType(typeof(MealPlanRuleResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRule([Required] long id)
        {
            var response = await _mealPlanOrchestrationService.GetRuleAsync(id);
            if (response == null)
                return NotFound();

            if (!IsHouseholdMember(response.HouseholdId))
                return Forbid();

            return Ok(response);
        }

        [HttpDelete("rule/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteRule([Required] long id)
        {
            var rule = await _mealPlanOrchestrationService.GetRuleAsync(id);
            if (rule == null)
                return NotFound();

            if (!IsHouseholdMember(rule.HouseholdId))
                return Forbid();

            await _mealPlanOrchestrationService.DeleteRuleAsync(id);
            return Ok(new { Message = "Meal plan rule deleted successfully." });
        }

        [HttpGet("week")]
        [ProducesResponseType(typeof(MealPlanWeekResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWeek(
            [Required][FromQuery] long householdId,
            [Required][FromQuery] DateOnly weekStart)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var response = await _mealPlanOrchestrationService.GetWeekAsync(householdId, weekStart);
            return Ok(response);
        }

        [HttpPost("exclusion")]
        [ProducesResponseType(typeof(MealPlanExclusionResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateExclusion([FromBody] MealPlanExclusionCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

            var response = await _mealPlanOrchestrationService.CreateExclusionAsync(model);
            return Created($"api/mealplan/exclusion/{response.Id}", response);
        }

        [HttpGet("exclusion")]
        [ProducesResponseType(typeof(List<MealPlanExclusionResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetExclusions(
            [Required][FromQuery] long householdId,
            [Required][FromQuery] DateOnly startDate,
            [Required][FromQuery] DateOnly endDate)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var response = await _mealPlanOrchestrationService.GetExclusionsAsync(householdId, startDate, endDate);
            return Ok(response);
        }

        [HttpDelete("exclusion/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteExclusion([Required] long id)
        {
            var exclusion = await _mealPlanOrchestrationService.GetExclusionAsync(id);
            if (exclusion == null)
                return NotFound();

            if (!IsHouseholdMember(exclusion.HouseholdId))
                return Forbid();

            await _mealPlanOrchestrationService.DeleteExclusionAsync(id);
            return Ok(new { Message = "Exclusion deleted successfully." });
        }

        [HttpPut("{id}/complete")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CompleteMealPlan([Required] long id)
        {
            var existing = await _mealPlanOrchestrationService.GetMealPlanAsync(id);
            if (existing == null)
                return NotFound(new { message = "Meal plan not found" });

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var success = await _pantryService.DeductFromPantryAsync(id);
            if (!success)
                return NotFound(new { message = "Meal plan not found or has no recipe" });

            return Ok(new { message = "Meal completed and pantry updated" });
        }
    }
}
