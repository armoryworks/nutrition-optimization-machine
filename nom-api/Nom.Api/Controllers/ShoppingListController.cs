using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Shopping;
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
        private readonly ApplicationDbContext _db;

        public ShoppingListController(
            IShoppingListOrchestrationService shoppingListOrchestrationService,
            ILogger<ShoppingListController> logger,
            ApplicationDbContext db)
        {
            _shoppingListOrchestrationService = shoppingListOrchestrationService;
            _logger = logger;
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<List<ShoppingListResponseModel>>> GetShoppingLists()
        {
            var shoppingLists = await _shoppingListOrchestrationService.GetAllShoppingListsAsync();
            return Ok(shoppingLists);
        }

        [HttpPost]
        [ProducesResponseType(typeof(ShoppingListCreateResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateShoppingList([FromBody] ShoppingListCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var authorId = GetCurrentPersonIdRequired();
            var response = await _shoppingListOrchestrationService.CreateShoppingListAsync(model, authorId);
            return CreatedAtAction(nameof(GetShoppingList), new { id = response.Id }, response);
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ShoppingListResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetShoppingList([Required] long id)
        {
            var response = await _shoppingListOrchestrationService.GetShoppingListAsync(id);
            if (response == null)
            {
                return NotFound();
            }
            return Ok(response);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ShoppingListResponseModel>> UpdateShoppingList(long id, [FromBody] ShoppingListUpdateModel request)
        {
            var response = await _shoppingListOrchestrationService.UpdateShoppingListAsync(id, request);
            if (response == null)
            {
                _logger.LogWarning("Shopping list with ID {ShoppingListId} not found for update.", id);
                return NotFound(new { message = "Shopping list not found" });
            }
            return Ok(response);
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteShoppingList([Required] long id)
        {
            var success = await _shoppingListOrchestrationService.DeleteShoppingListAsync(id);
            if (!success)
            {
                return NotFound();
            }
            return Ok(new { Message = "Shopping list deleted successfully." });
        }

        [HttpPost("item")]
        [ProducesResponseType(typeof(ShoppingListItemResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AddItem([FromBody] ShoppingListItemCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _shoppingListOrchestrationService.AddItemAsync(model);
            return Created($"api/shoppinglist/item/{response.Id}", response);
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

            var response = await _shoppingListOrchestrationService.UpdateItemAsync(id, model);
            if (response == null)
            {
                return NotFound();
            }
            return Ok(response);
        }

        [HttpDelete("item/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteItem([Required] long id)
        {
            var success = await _shoppingListOrchestrationService.DeleteItemAsync(id);
            if (!success)
            {
                return NotFound();
            }
            return Ok(new { Message = "Shopping list item deleted successfully." });
        }

        // Recipe Integration Endpoints
        [HttpPost("{id}/recipe/{recipeId}")]
        public async Task<ActionResult<ShoppingListResponseModel>> AddRecipeIngredients(long id, long recipeId, [FromBody] ShoppingListRecipeAddModel request)
        {
            request.ShoppingListId = id;
            request.RecipeId = recipeId;
            var response = await _shoppingListOrchestrationService.AddRecipeIngredientsAsync(request);
            return Ok(response);
        }

        [HttpDelete("{id}/recipe/{recipeId}")]
        public async Task<ActionResult<ShoppingListResponseModel>> RemoveRecipeIngredients(long id, long recipeId, [FromBody] ShoppingListRecipeRemoveModel request)
        {
            request.ShoppingListId = id;
            request.RecipeId = recipeId;
            var response = await _shoppingListOrchestrationService.RemoveRecipeIngredientsAsync(request);
            return Ok(response);
        }

        [HttpPost("{id}/share")]
        public async Task<IActionResult> ShareShoppingList(long id, [FromBody] ShoppingListShareRequest request)
        {
            var exists = await _db.ShoppingListShares
                .AnyAsync(s => s.ShoppingListId == id && s.PersonId == request.PersonId);

            if (exists)
                return Conflict(new { message = "Shopping list is already shared with this person." });

            _db.ShoppingListShares.Add(new ShoppingListShareEntity
            {
                ShoppingListId = id,
                PersonId = request.PersonId
            });
            await _db.SaveChangesAsync();
            return Created($"api/shoppinglist/{id}/share", new { message = "Shopping list shared successfully." });
        }

        [HttpDelete("{id}/share/{personId}")]
        public async Task<IActionResult> UnshareShoppingList(long id, long personId)
        {
            var share = await _db.ShoppingListShares
                .FirstOrDefaultAsync(s => s.ShoppingListId == id && s.PersonId == personId);

            if (share == null)
                return NotFound(new { message = "Share not found." });

            _db.ShoppingListShares.Remove(share);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Shopping list unshared successfully." });
        }
    }
}