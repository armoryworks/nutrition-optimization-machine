// File: Nom.Api/Controllers/DietAdminController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin management of diet/restriction categories (reference rows in the
    /// diet groups) and the filter criteria that give them teeth.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CanManageCuration")]
    public class DietAdminController : BaseApiController
    {
        private readonly IDietAdminOrchestrationService _dietAdminService;

        public DietAdminController(IDietAdminOrchestrationService dietAdminService)
        {
            _dietAdminService = dietAdminService;
        }

        [HttpGet("groups")]
        public async Task<ActionResult<List<RestrictionGroupModel>>> GetGroups()
            => Ok(await _dietAdminService.GetGroupsAsync());

        [HttpPost("categories")]
        public async Task<ActionResult<RestrictionCategoryModel>> CreateCategory([FromBody] CreateRestrictionCategoryRequest request)
        {
            var created = await _dietAdminService.CreateCategoryAsync(request);
            if (created == null)
                return BadRequest(new { message = "Unknown or non-diet group" });
            return Ok(created);
        }

        [HttpPut("categories/{id}")]
        public async Task<ActionResult<RestrictionCategoryModel>> UpdateCategory(long id, [FromBody] UpdateRestrictionCategoryRequest request)
        {
            var updated = await _dietAdminService.UpdateCategoryAsync(id, request);
            if (updated == null)
                return NotFound(new { message = "Category not found" });
            return Ok(updated);
        }

        [HttpDelete("categories/{id}")]
        public async Task<ActionResult> DeleteCategory(long id)
        {
            var result = await _dietAdminService.DeleteCategoryAsync(id);
            if (result == null)
                return NotFound(new { message = "Category not found" });
            if (result == false)
                return Conflict(new { message = "Category is referenced by existing restrictions" });
            return NoContent();
        }

        [HttpGet("categories/{id}/criteria")]
        public async Task<ActionResult<List<RestrictionCriterionModel>>> GetCriteria(long id)
            => Ok(await _dietAdminService.GetCriteriaAsync(id));

        [HttpPost("categories/{id}/criteria")]
        public async Task<ActionResult<RestrictionCriterionModel>> AddCriterion(long id, [FromBody] SaveRestrictionCriterionRequest request)
        {
            var created = await _dietAdminService.AddCriterionAsync(id, request);
            if (created == null)
                return BadRequest(new { message = "Category not found, or criterion needs an ingredient, pattern, or nutrient" });
            return Ok(created);
        }

        [HttpDelete("criteria/{criterionId}")]
        public async Task<ActionResult> DeleteCriterion(long criterionId)
        {
            var removed = await _dietAdminService.DeleteCriterionAsync(criterionId);
            return removed ? NoContent() : NotFound(new { message = "Criterion not found" });
        }
    }
}
