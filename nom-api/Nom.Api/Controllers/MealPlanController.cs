using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MealPlanController : BaseApiController
    {
        private readonly IMealPlanOrchestrationService _mealPlanOrchestrationService;
        private readonly ILogger<MealPlanController> _logger;

        public MealPlanController(
            IMealPlanOrchestrationService mealPlanOrchestrationService,
            ILogger<MealPlanController> logger)
        {
            _mealPlanOrchestrationService = mealPlanOrchestrationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<MealPlanResponseModel>>> GetMealPlans()
        {
            try
            {
                var mealPlans = await _mealPlanOrchestrationService.GetAllMealPlansAsync();
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
                var response = await _mealPlanOrchestrationService.CreateMealPlanAsync(model);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateMealPlan.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}")]
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

        [HttpPut("{id}")]
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

        [HttpDelete("{id}")]
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
    }
} 