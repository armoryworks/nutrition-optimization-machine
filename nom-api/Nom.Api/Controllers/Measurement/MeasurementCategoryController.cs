using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Controllers;
using Nom.Orch.Interfaces.Measurement;
using Nom.Orch.Models.Measurement;

namespace Nom.Api.Controllers.Measurement
{
    /// <summary>
    /// API controller for managing measurement categories.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MeasurementCategoryController : BaseApiController
    {
        private readonly IMeasurementCategoryOrchestrationService _categoryOrchestrationService;

        public MeasurementCategoryController(
            IMeasurementCategoryOrchestrationService categoryOrchestrationService)
        {
            _categoryOrchestrationService = categoryOrchestrationService;
        }

        /// <summary>
        /// Gets all measurement categories.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<MeasurementCategoryModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllCategories()
        {
            var categories = await _categoryOrchestrationService.GetAllCategoriesAsync();
            return Ok(categories);
        }

        /// <summary>
        /// Gets a measurement category by its ID.
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(MeasurementCategoryModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCategoryById(long id)
        {
            var category = await _categoryOrchestrationService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound(new { message = $"Measurement category with ID {id} not found." });
            }

            return Ok(category);
        }

        /// <summary>
        /// Creates a new measurement category.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(MeasurementCategoryModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = await _categoryOrchestrationService.CreateCategoryAsync(request);
            return CreatedAtAction(nameof(GetCategoryById), new { id = category.Id }, category);
        }

        /// <summary>
        /// Updates an existing measurement category.
        /// </summary>
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateCategory(long id, [FromBody] UpdateCategoryRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Ensure the ID in the request matches the route parameter
            if (request.Id != id)
            {
                return BadRequest(new { message = "ID in request body must match route parameter." });
            }

            var updated = await _categoryOrchestrationService.UpdateCategoryAsync(request);
            if (!updated)
            {
                return NotFound(new { message = $"Measurement category with ID {id} not found." });
            }

            return Ok(new { message = "Measurement category updated successfully." });
        }

        /// <summary>
        /// Deletes a measurement category.
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCategory(long id)
        {
            var deleted = await _categoryOrchestrationService.DeleteCategoryAsync(id);
            if (!deleted)
            {
                return NotFound(new { message = $"Measurement category with ID {id} not found." });
            }

            return Ok(new { message = "Measurement category deleted successfully." });
        }

        /// <summary>
        /// Gets all measurements in a specific category.
        /// </summary>
        [HttpGet("{id}/measurements")]
        [ProducesResponseType(typeof(List<MeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMeasurementsInCategory(long id)
        {
            var measurements = await _categoryOrchestrationService.GetMeasurementsInCategoryAsync(id);
            return Ok(measurements);
        }

        /// <summary>
        /// Sets the base unit for a category.
        /// </summary>
        [HttpPut("{id}/base-unit/{measurementId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetBaseUnit(long id, long measurementId)
        {
            var set = await _categoryOrchestrationService.SetBaseUnitAsync(id, measurementId);
            if (!set)
            {
                return NotFound(new { message = $"Category or measurement not found, or measurement does not belong to category." });
            }

            return Ok(new { message = "Base unit set successfully." });
        }
    }
}
