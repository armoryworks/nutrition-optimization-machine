using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<MeasurementController> _logger;

        public MeasurementController(
            IMeasurementOrchestrationService measurementOrchestrationService,
            ILogger<MeasurementController> logger)
        {
            _measurementOrchestrationService = measurementOrchestrationService;
            _logger = logger;
        }

        /// <summary>
        /// Gets all measurements for a specific category.
        /// </summary>
        [HttpGet("by-category/{categoryId}")]
        [ProducesResponseType(typeof(List<MeasurementModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetMeasurementsByCategory(long categoryId)
        {
            try
            {
                var measurements = await _measurementOrchestrationService.GetMeasurementsByCategoryAsync(categoryId);
                return Ok(measurements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements for category {CategoryId}", categoryId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving measurements." });
            }
        }

        /// <summary>
        /// Gets a measurement by its ID.
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(MeasurementModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMeasurementById(long id)
        {
            try
            {
                var measurement = await _measurementOrchestrationService.GetMeasurementByIdAsync(id);
                if (measurement == null)
                {
                    return NotFound(new { message = $"Measurement with ID {id} not found." });
                }

                return Ok(measurement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurement with ID {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving the measurement." });
            }
        }

        /// <summary>
        /// Converts a value from one measurement unit to another.
        /// </summary>
        [HttpGet("convert")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ConvertMeasurement([FromQuery] long fromId, [FromQuery] long toId, [FromQuery] decimal value)
        {
            try
            {
                var convertedValue = await _measurementOrchestrationService.ConvertMeasurementAsync(fromId, toId, value);
                return Ok(convertedValue);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting measurement {Value} from {FromId} to {ToId}", value, fromId, toId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while converting the measurement." });
            }
        }

        /// <summary>
        /// Bulk converts multiple measurement values efficiently.
        /// </summary>
        [HttpPost("bulk-convert")]
        [ProducesResponseType(typeof(List<decimal>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> BulkConvertMeasurements([FromBody] List<BulkConversionRequest> requests)
        {
            try
            {
                if (requests == null || !requests.Any())
                {
                    return BadRequest(new { message = "At least one conversion request is required." });
                }

                var conversions = requests.Select(r => (r.FromId, r.ToId, r.Value)).ToList();
                var results = await _measurementOrchestrationService.BulkConvertMeasurementsAsync(conversions);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in bulk conversion of {Count} measurements", requests?.Count ?? 0);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while performing bulk conversion." });
            }
        }

        /// <summary>
        /// Gets performance statistics for the measurement system.
        /// </summary>
        [HttpGet("performance-stats")]
        [ProducesResponseType(typeof(MeasurementPerformanceStats), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPerformanceStats()
        {
            try
            {
                // This would require injecting IMeasurementPerformanceMonitor into the controller
                // For now, we'll return a placeholder
                return Ok(new { message = "Performance monitoring endpoint - implementation pending" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving performance statistics");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving performance statistics." });
            }
        }

        /// <summary>
        /// Gets conversion paths between two measurement units.
        /// </summary>
        [HttpGet("conversions")]
        [ProducesResponseType(typeof(List<MeasurementConversionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetConversionPaths([FromQuery] long fromId, [FromQuery] long toId)
        {
            try
            {
                var conversions = await _measurementOrchestrationService.GetConversionPathsAsync(fromId, toId);
                return Ok(conversions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversion paths from {FromId} to {ToId}", fromId, toId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving conversion paths." });
            }
        }

        /// <summary>
        /// Gets all measurements for a specific ingredient.
        /// </summary>
        [HttpGet("ingredient/{ingredientId}")]
        [ProducesResponseType(typeof(List<IngredientMeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetIngredientMeasurements(long ingredientId)
        {
            try
            {
                var measurements = await _measurementOrchestrationService.GetIngredientMeasurementsAsync(ingredientId);
                return Ok(measurements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements for ingredient {IngredientId}", ingredientId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving ingredient measurements." });
            }
        }

        /// <summary>
        /// Gets all measurements for a specific nutrient.
        /// </summary>
        [HttpGet("nutrient/{nutrientId}")]
        [ProducesResponseType(typeof(List<NutrientMeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetNutrientMeasurements(long nutrientId)
        {
            try
            {
                var measurements = await _measurementOrchestrationService.GetNutrientMeasurementsAsync(nutrientId);
                return Ok(measurements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements for nutrient {NutrientId}", nutrientId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving nutrient measurements." });
            }
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

            try
            {
                var measurement = await _measurementOrchestrationService.CreateMeasurementAsync(request);
                return CreatedAtAction(nameof(GetMeasurementById), new { id = measurement.Id }, measurement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating measurement {Name}", request.Name);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while creating the measurement." });
            }
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

            try
            {
                var conversion = await _measurementOrchestrationService.CreateConversionAsync(request);
                return CreatedAtAction(nameof(GetConversionPaths), new { fromId = request.FromMeasurementId, toId = request.ToMeasurementId }, conversion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating conversion from {FromId} to {ToId}", request.FromMeasurementId, request.ToMeasurementId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while creating the conversion." });
            }
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

            try
            {
                var measurement = await _measurementOrchestrationService.UpdateMeasurementAsync(id, request);
                return Ok(measurement);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating measurement {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while updating the measurement." });
            }
        }

        /// <summary>
        /// Deletes a measurement.
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteMeasurement(long id)
        {
            try
            {
                var deleted = await _measurementOrchestrationService.DeleteMeasurementAsync(id);
                if (!deleted)
                {
                    return NotFound(new { message = $"Measurement with ID {id} not found." });
                }

                return Ok(new { message = "Measurement deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting measurement {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while deleting the measurement." });
            }
        }

        /// <summary>
        /// Gets all measurements.
        /// </summary>
        [HttpGet("all")]
        [ProducesResponseType(typeof(List<MeasurementModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllMeasurements()
        {
            try
            {
                var measurements = await _measurementOrchestrationService.GetAllMeasurementsAsync();
                return Ok(measurements);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all measurements");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving measurements." });
            }
        }

        /// <summary>
        /// Gets all measurement categories.
        /// </summary>
        [HttpGet("categories")]
        [ProducesResponseType(typeof(List<MeasurementCategoryModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllCategories()
        {
            try
            {
                var categories = await _measurementOrchestrationService.GetAllCategoriesAsync();
                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurement categories");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving measurement categories." });
            }
        }

        /// <summary>
        /// Gets a measurement category by its ID.
        /// </summary>
        [HttpGet("category-details/{id}")]
        [ProducesResponseType(typeof(MeasurementCategoryModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCategoryById(long id)
        {
            try
            {
                var category = await _measurementOrchestrationService.GetCategoryByIdAsync(id);
                if (category == null)
                {
                    return NotFound(new { message = $"Measurement category with ID {id} not found." });
                }

                return Ok(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurement category with ID {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred while retrieving the measurement category." });
            }
        }
    }
}
