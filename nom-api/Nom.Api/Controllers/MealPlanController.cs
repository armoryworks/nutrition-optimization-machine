using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Models.Pantry;
using Nom.Orch.Models.Plan;
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
        private readonly IPortionOrchestrationService _portionService;

        public MealPlanController(
            IMealPlanOrchestrationService mealPlanOrchestrationService,
            IPantryOrchestrationService pantryService,
            IPortionOrchestrationService portionService)
        {
            _mealPlanOrchestrationService = mealPlanOrchestrationService;
            _pantryService = pantryService;
            _portionService = portionService;
        }

        /// <summary>
        /// Per-member portion breakdown for one planned meal cell: cook factor
        /// per recipe and plates per member, from macro-goal calorie targets
        /// and the household meal split.
        /// </summary>
        [HttpGet("portions")]
        public async Task<ActionResult<PortionBreakdownModel>> GetPortions(
            [Required] long householdId, [Required] DateOnly date, [Required] long mealTypeId)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var breakdown = await _portionService.ComputePortionsAsync(householdId, date, mealTypeId);
            if (breakdown == null)
                return NotFound(new { message = "No planned recipes in that meal slot" });
            return Ok(breakdown);
        }

        /// <summary>
        /// Cook factors for every planned recipe in a date range — used by the
        /// shopping list to scale ingredient quantities to household portions.
        /// </summary>
        [HttpGet("portions/range")]
        public async Task<ActionResult<List<RangeCookFactorModel>>> GetPortionRange(
            [Required] long householdId, [Required] DateOnly startDate, [Required] DateOnly endDate)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var factors = await _portionService.ComputeRangeCookFactorsAsync(householdId, startDate, endDate);
            return Ok(factors);
        }

        /// <summary>
        /// 403 body for policy feature gates, matching the {message, reason}
        /// contract used by RecipeController/HouseholdPolicyController.
        /// Without this the orchestration's UnauthorizedAccessException would
        /// surface as a 401 ProblemDetails and be mistaken for session expiry.
        /// </summary>
        private ObjectResult FeatureGated(string reason) =>
            StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "This feature is disabled by your household policy.",
                reason
            });

        /// <summary>409 body for locked-restriction (and visibility) conflicts.</summary>
        private ObjectResult RestrictionViolation(string reason) =>
            Conflict(new
            {
                message = "This recipe conflicts with a locked dietary restriction in your household.",
                reason
            });

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
            try
            {
                var response = await _mealPlanOrchestrationService.CreateMealPlanAsync(model, authorId);
                return CreatedAtAction(nameof(GetMealPlan), new { id = response.Id }, response);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("restriction_violation"))
            {
                return RestrictionViolation(ex.Message);
            }
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

            try
            {
                var response = await _mealPlanOrchestrationService.UpdateMealPlanAsync(id, model);
                return Ok(response);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("restriction_violation"))
            {
                return RestrictionViolation(ex.Message);
            }
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
            try
            {
                var response = await _mealPlanOrchestrationService.ShuffleMealPlansAsync(model, authorId);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex) when (ex.Message.StartsWith("feature_gated"))
            {
                return FeatureGated(ex.Message);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("restriction_violation"))
            {
                return RestrictionViolation(ex.Message);
            }
        }

        /// <summary>Lists the available nutritional food groups (Vegetables, Fruits, …).</summary>
        [HttpGet("food-groups")]
        [ProducesResponseType(typeof(List<FoodGroupModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFoodGroups()
        {
            return Ok(await _mealPlanOrchestrationService.GetFoodGroupsAsync());
        }

        /// <summary>Gets a household's food-group requirements (min servings per day/meal).</summary>
        [HttpGet("household/{householdId:long}/food-group-rules")]
        [ProducesResponseType(typeof(List<FoodGroupRuleModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFoodGroupRules([Required] long householdId)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();
            return Ok(await _mealPlanOrchestrationService.GetFoodGroupRulesAsync(householdId));
        }

        /// <summary>Creates or updates a household food-group requirement (steward/manager only).</summary>
        [HttpPut("food-group-rule")]
        [ProducesResponseType(typeof(FoodGroupRuleModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UpsertFoodGroupRule([FromBody] FoodGroupRuleUpsertModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            if (!CanManageHousehold(model.HouseholdId))
                return Forbid();
            try
            {
                return Ok(await _mealPlanOrchestrationService.UpsertFoodGroupRuleAsync(model));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Deletes a household food-group requirement (steward/manager only).</summary>
        [HttpDelete("food-group-rule/{id:long}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteFoodGroupRule([Required] long id, [FromQuery, Required] long householdId)
        {
            if (!CanManageHousehold(householdId))
                return Forbid();
            var ok = await _mealPlanOrchestrationService.DeleteFoodGroupRuleAsync(id);
            return ok ? Ok() : NotFound();
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

        [HttpPut("{id}/shopping-completed")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MarkShoppingCompleted([Required] long id)
        {
            var existing = await _mealPlanOrchestrationService.GetMealPlanAsync(id);
            if (existing == null)
                return NotFound(new { message = "Meal plan entry not found" });

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var success = await _mealPlanOrchestrationService.MarkShoppingCompletedAsync(id);
            if (!success)
                return NotFound(new { message = "Meal plan entry not found" });

            return Ok(new { message = "Shopping marked as completed" });
        }
    }
}
