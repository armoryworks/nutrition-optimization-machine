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
        private readonly ILogger<MealPlanController> _logger;

        public MealPlanController(
            IMealPlanOrchestrationService mealPlanOrchestrationService,
            IPantryOrchestrationService pantryService,
            ILogger<MealPlanController> logger)
        {
            _mealPlanOrchestrationService = mealPlanOrchestrationService;
            _pantryService = pantryService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<MealPlanResponseModel>>> GetMealPlans(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var mealPlans = await _mealPlanOrchestrationService.GetAllMealPlansAsync(startDate, endDate);
                return Ok(mealPlans);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetMealPlans.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to retrieve meal plans", error = ex.Message });
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(MealPlanCreateResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateMealPlan([FromBody] MealPlanCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var authorId = GetCurrentPersonIdRequired();
                var response = await _mealPlanOrchestrationService.CreateMealPlanAsync(model, authorId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateMealPlan.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(MealPlanResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMealPlan([Required] long id)
        {
            try
            {
                var response = await _mealPlanOrchestrationService.GetMealPlanAsync(id);
                if (response == null)
                {
                    return NotFound();
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetMealPlan.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
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

            try
            {
                var response = await _mealPlanOrchestrationService.UpdateMealPlanAsync(id, model);
                if (response == null)
                {
                    return NotFound();
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpdateMealPlan.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("{id:long}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteMealPlan([Required] long id)
        {
            try
            {
                var success = await _mealPlanOrchestrationService.DeleteMealPlanAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return Ok(new { Message = "Meal plan deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteMealPlan.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
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

            try
            {
                var authorId = GetCurrentPersonIdRequired();
                var response = await _mealPlanOrchestrationService.ShuffleMealPlansAsync(model, authorId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in ShuffleMealPlans.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("rule")]
        [ProducesResponseType(typeof(MealPlanRuleCreateResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateRule([FromBody] MealPlanRuleCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var response = await _mealPlanOrchestrationService.CreateRuleAsync(model);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateRule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("rule/{id}")]
        [ProducesResponseType(typeof(MealPlanRuleResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRule([Required] long id)
        {
            try
            {
                var response = await _mealPlanOrchestrationService.GetRuleAsync(id);
                if (response == null)
                {
                    return NotFound();
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("rule/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteRule([Required] long id)
        {
            try
            {
                var success = await _mealPlanOrchestrationService.DeleteRuleAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return Ok(new { Message = "Meal plan rule deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteRule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("week")]
        [ProducesResponseType(typeof(MealPlanWeekResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWeek(
            [Required][FromQuery] long householdId,
            [Required][FromQuery] DateOnly weekStart)
        {
            try
            {
                var response = await _mealPlanOrchestrationService.GetWeekAsync(householdId, weekStart);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetWeek.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("exclusion")]
        [ProducesResponseType(typeof(MealPlanExclusionResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateExclusion([FromBody] MealPlanExclusionCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var response = await _mealPlanOrchestrationService.CreateExclusionAsync(model);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateExclusion.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("exclusion")]
        [ProducesResponseType(typeof(List<MealPlanExclusionResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetExclusions(
            [Required][FromQuery] long householdId,
            [Required][FromQuery] DateOnly startDate,
            [Required][FromQuery] DateOnly endDate)
        {
            try
            {
                var response = await _mealPlanOrchestrationService.GetExclusionsAsync(householdId, startDate, endDate);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetExclusions.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("exclusion/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteExclusion([Required] long id)
        {
            try
            {
                var success = await _mealPlanOrchestrationService.DeleteExclusionAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return Ok(new { Message = "Exclusion deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteExclusion.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("{id}/complete")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CompleteMealPlan([Required] long id)
        {
            try
            {
                var success = await _pantryService.DeductFromPantryAsync(id);
                if (!success)
                    return NotFound(new { message = "Meal plan not found or has no recipe" });

                return Ok(new { message = "Meal completed and pantry updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing meal plan {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Failed to complete meal plan" });
            }
        }
    }
} 