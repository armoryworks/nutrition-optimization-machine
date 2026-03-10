using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Controllers;
using Nom.Orch.Interfaces.Measurement;
using Nom.Orch.Models.Measurement;
using Nom.Orch.Services.Measurement;

namespace Nom.Api.Controllers.Measurement
{
    /// <summary>
    /// API controller for managing measurements and conversions.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MeasurementController : BaseApiController
    {
        private readonly IMeasurementOrchestrationService _measurementOrchestrationService;

        public MeasurementController(
            IMeasurementOrchestrationService measurementOrchestrationService)
        {
            _measurementOrchestrationService = measurementOrchestrationService;
        }

        /// <summary>
        /// Gets all measurements for a specific category.
        /// </summary>
        [HttpGet("by-category/{categoryId}")]
        [ProducesResponseType(typeof(List<MeasurementModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetMeasurementsByCategory(long categoryId)
        {
            var measurements = await _measurementOrchestrationService.GetMeasurementsByCategoryAsync(categoryId);
            return Ok(measurements);
        }

        /// <summary>
        /// Gets a measurement by its ID.
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(MeasurementModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMeasurementById(long id)
        {
            var measurement = await _measurementOrchestrationService.GetMeasurementByIdAsync(id);
            if (measurement == null)
            {
                return NotFound(new { message = $"Measurement with ID {id} not found." });
            }

            return Ok(measurement);
        }

        /// <summary>
        /// Converts a value from one measurement unit to another.
        /// </summary>
        [HttpGet("convert")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ConvertMeasurement([FromQuery] long fromId, [FromQuery] long toId, [FromQuery] decimal value)
        {
            var convertedValue = await _measurementOrchestrationService.ConvertMeasurementAsync(fromId, toId, value);
            return Ok(convertedValue);
        }

        /// <summary>
        /// Bulk converts multiple measurement values efficiently.
        /// </summary>
        [HttpPost("bulk-convert")]
        [ProducesResponseType(typeof(List<decimal>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> BulkConvertMeasurements([FromBody] List<BulkConversionRequest> requests)
        {
            if (requests == null || !requests.Any())
            {
                return BadRequest(new { message = "At least one conversion request is required." });
            }

            var conversions = requests.Select(r => (r.FromId, r.ToId, r.Value)).ToList();
            var results = await _measurementOrchestrationService.BulkConvertMeasurementsAsync(conversions);
            return Ok(results);
        }

        /// <summary>
        /// Gets performance statistics for the measurement system.
        /// </summary>
        [HttpGet("performance-stats")]
        [ProducesResponseType(typeof(MeasurementPerformanceStats), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPerformanceStats()
        {
            // This would require injecting IMeasurementPerformanceMonitor into the controller
            // For now, we'll return a placeholder
            return Ok(new { message = "Performance monitoring endpoint - implementation pending" });
        }

        /// <summary>
        /// Gets conversion paths between two measurement units.
        /// </summary>
        [HttpGet("conversions")]
        [ProducesResponseType(typeof(List<MeasurementConversionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetConversionPaths([FromQuery] long fromId, [FromQuery] long toId)
        {
            var conversions = await _measurementOrchestrationService.GetConversionPathsAsync(fromId, toId);
            return Ok(conversions);
        }

        /// <summary>
        /// Gets all measurements for a specific ingredient.
        /// </summary>
        [HttpGet("ingredient/{ingredientId}")]
        [ProducesResponseType(typeof(List<IngredientMeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetIngredientMeasurements(long ingredientId)
        {
            var measurements = await _measurementOrchestrationService.GetIngredientMeasurementsAsync(ingredientId);
            return Ok(measurements);
        }

        /// <summary>
        /// Gets all measurements for a specific nutrient.
        /// </summary>
        [HttpGet("nutrient/{nutrientId}")]
        [ProducesResponseType(typeof(List<NutrientMeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetNutrientMeasurements(long nutrientId)
        {
            var measurements = await _measurementOrchestrationService.GetNutrientMeasurementsAsync(nutrientId);
            return Ok(measurements);
        }

        /// <summary>
        /// Creates a new measurement.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(MeasurementModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateMeasurement([FromBody] CreateMeasurementRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var measurement = await _measurementOrchestrationService.CreateMeasurementAsync(request);
            return CreatedAtAction(nameof(GetMeasurementById), new { id = measurement.Id }, measurement);
        }

        /// <summary>
        /// Creates a new conversion rule.
        /// </summary>
        [HttpPost("conversion")]
        [ProducesResponseType(typeof(MeasurementConversionModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateConversion([FromBody] CreateConversionRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var conversion = await _measurementOrchestrationService.CreateConversionAsync(request);
            return CreatedAtAction(nameof(GetConversionPaths), new { fromId = request.FromMeasurementId, toId = request.ToMeasurementId }, conversion);
        }

        /// <summary>
        /// Updates an existing measurement.
        /// </summary>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(MeasurementModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateMeasurement(long id, [FromBody] UpdateMeasurementRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var measurement = await _measurementOrchestrationService.UpdateMeasurementAsync(id, request);
            return Ok(measurement);
        }

        /// <summary>
        /// Deletes a measurement.
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteMeasurement(long id)
        {
            var deleted = await _measurementOrchestrationService.DeleteMeasurementAsync(id);
            if (!deleted)
            {
                return NotFound(new { message = $"Measurement with ID {id} not found." });
            }

            return Ok(new { message = "Measurement deleted successfully." });
        }

        /// <summary>
        /// Gets all measurements.
        /// </summary>
        [HttpGet("all")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<MeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllMeasurements()
        {
            var measurements = await _measurementOrchestrationService.GetAllMeasurementsAsync();
            return Ok(measurements);
        }

        /// <summary>
        /// Gets all measurement categories.
        /// </summary>
        [HttpGet("categories")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<MeasurementCategoryModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllCategories()
        {
            var categories = await _measurementOrchestrationService.GetAllCategoriesAsync();
            return Ok(categories);
        }

        /// <summary>
        /// Gets a measurement category by its ID.
        /// </summary>
        [HttpGet("category-details/{id}")]
        [ProducesResponseType(typeof(MeasurementCategoryModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCategoryById(long id)
        {
            var category = await _measurementOrchestrationService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound(new { message = $"Measurement category with ID {id} not found." });
            }

            return Ok(category);
        }
    }
}
