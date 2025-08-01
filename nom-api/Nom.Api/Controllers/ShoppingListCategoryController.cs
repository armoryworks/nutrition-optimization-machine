using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ShoppingListCategoryController : BaseApiController
    {
        private readonly IShoppingListCategoryOrchestrationService _categoryOrchestrationService;
        private readonly ILogger<ShoppingListCategoryController> _logger;

        public ShoppingListCategoryController(
            IShoppingListCategoryOrchestrationService categoryOrchestrationService,
            ILogger<ShoppingListCategoryController> logger)
        {
            _categoryOrchestrationService = categoryOrchestrationService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<ShoppingListCategoryResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var categories = await _categoryOrchestrationService.GetAllCategoriesAsync();
                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetCategories.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ShoppingListCategoryResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCategory([Required] long id)
        {
            try
            {
                var category = await _categoryOrchestrationService.GetCategoryAsync(id);
                if (category == null)
                {
                    return NotFound();
                }
                return Ok(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetCategory.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(ShoppingListCategoryResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateCategory([FromBody] ShoppingListCategoryCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var response = await _categoryOrchestrationService.CreateCategoryAsync(model);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateCategory.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(ShoppingListCategoryResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateCategory([Required] long id, [FromBody] ShoppingListCategoryCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var response = await _categoryOrchestrationService.UpdateCategoryAsync(id, model);
                if (response == null)
                {
                    return NotFound();
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpdateCategory.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCategory([Required] long id)
        {
            try
            {
                var success = await _categoryOrchestrationService.DeleteCategoryAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return Ok(new { Message = "Category deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteCategory.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("bulk-operation")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> BulkOperation([FromBody] ShoppingListBulkOperationModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var success = await _categoryOrchestrationService.MoveItemsToCategoryAsync(model);
                if (success)
                {
                    return Ok(new { Message = "Bulk operation completed successfully." });
                }
                return BadRequest(new { Message = "Bulk operation failed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in BulkOperation.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }
    }
} 