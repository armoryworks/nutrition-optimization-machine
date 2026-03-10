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

        public ShoppingListCategoryController(
            IShoppingListCategoryOrchestrationService categoryOrchestrationService)
        {
            _categoryOrchestrationService = categoryOrchestrationService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<ShoppingListCategoryResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _categoryOrchestrationService.GetAllCategoriesAsync();
            return Ok(categories);
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ShoppingListCategoryResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCategory([Required] long id)
        {
            var category = await _categoryOrchestrationService.GetCategoryAsync(id);
            if (category == null)
            {
                return NotFound();
            }
            return Ok(category);
        }

        [HttpPost]
        [ProducesResponseType(typeof(ShoppingListCategoryResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateCategory([FromBody] ShoppingListCategoryCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _categoryOrchestrationService.CreateCategoryAsync(model);
            return CreatedAtAction(nameof(GetCategory), new { id = response.Id }, response);
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

            var response = await _categoryOrchestrationService.UpdateCategoryAsync(id, model);
            if (response == null)
            {
                return NotFound();
            }
            return Ok(response);
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCategory([Required] long id)
        {
            var success = await _categoryOrchestrationService.DeleteCategoryAsync(id);
            if (!success)
            {
                return NotFound();
            }
            return Ok(new { Message = "Category deleted successfully." });
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

            var success = await _categoryOrchestrationService.MoveItemsToCategoryAsync(model);
            if (success)
            {
                return Ok(new { Message = "Bulk operation completed successfully." });
            }
            return BadRequest(new { Message = "Bulk operation failed." });
        }
    }
}