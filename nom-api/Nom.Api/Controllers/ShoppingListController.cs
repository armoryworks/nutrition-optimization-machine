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
    public class ShoppingListController : BaseApiController
    {
        private readonly IShoppingListOrchestrationService _shoppingListOrchestrationService;
        private readonly ILogger<ShoppingListController> _logger;

        public ShoppingListController(
            IShoppingListOrchestrationService shoppingListOrchestrationService,
            ILogger<ShoppingListController> logger)
        {
            _shoppingListOrchestrationService = shoppingListOrchestrationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<ShoppingListResponseModel>>> GetShoppingLists()
        {
            try
            {
                var shoppingLists = await _shoppingListOrchestrationService.GetAllShoppingListsAsync();
                return Ok(shoppingLists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetShoppingLists.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to retrieve shopping lists", error = ex.Message });
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(ShoppingListCreateResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateShoppingList([FromBody] ShoppingListCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var authorId = GetCurrentPersonIdRequired();
                var response = await _shoppingListOrchestrationService.CreateShoppingListAsync(model, authorId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateShoppingList.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ShoppingListResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetShoppingList([Required] long id)
        {
            try
            {
                var response = await _shoppingListOrchestrationService.GetShoppingListAsync(id);
                if (response == null)
                {
                    return NotFound();
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetShoppingList.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ShoppingListResponseModel>> UpdateShoppingList(long id, [FromBody] ShoppingListUpdateModel request)
        {
            try
            {
                var response = await _shoppingListOrchestrationService.UpdateShoppingListAsync(id, request);
                if (response == null)
                {
                    _logger.LogWarning("Shopping list with ID {ShoppingListId} not found for update.", id);
                    return NotFound(new { message = "Shopping list not found" });
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpdateShoppingList.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to update shopping list", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteShoppingList([Required] long id)
        {
            try
            {
                var success = await _shoppingListOrchestrationService.DeleteShoppingListAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return Ok(new { Message = "Shopping list deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteShoppingList.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("item")]
        [ProducesResponseType(typeof(ShoppingListItemResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AddItem([FromBody] ShoppingListItemCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var response = await _shoppingListOrchestrationService.AddItemAsync(model);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in AddItem.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("item/{id}")]
        [ProducesResponseType(typeof(ShoppingListItemResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateItem([Required] long id, [FromBody] ShoppingListItemUpdateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var response = await _shoppingListOrchestrationService.UpdateItemAsync(id, model);
                if (response == null)
                {
                    return NotFound();
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpdateItem.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("item/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteItem([Required] long id)
        {
            try
            {
                var success = await _shoppingListOrchestrationService.DeleteItemAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return Ok(new { Message = "Shopping list item deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteItem.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        // Recipe Integration Endpoints
        [HttpPost("{id}/recipe/{recipeId}")]
        public async Task<ActionResult<ShoppingListResponseModel>> AddRecipeIngredients(long id, long recipeId, [FromBody] ShoppingListRecipeAddModel request)
        {
            try
            {
                request.ShoppingListId = id;
                request.RecipeId = recipeId;
                var response = await _shoppingListOrchestrationService.AddRecipeIngredientsAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in AddRecipeIngredients.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to add recipe ingredients", error = ex.Message });
            }
        }

        [HttpDelete("{id}/recipe/{recipeId}")]
        public async Task<ActionResult<ShoppingListResponseModel>> RemoveRecipeIngredients(long id, long recipeId, [FromBody] ShoppingListRecipeRemoveModel request)
        {
            try
            {
                request.ShoppingListId = id;
                request.RecipeId = recipeId;
                var response = await _shoppingListOrchestrationService.RemoveRecipeIngredientsAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in RemoveRecipeIngredients.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to remove recipe ingredients", error = ex.Message });
            }
        }
    }
} 