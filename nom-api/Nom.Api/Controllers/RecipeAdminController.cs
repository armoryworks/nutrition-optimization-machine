// Nom.Api/Controllers/RecipeAdminController.cs
using Nom.Orch.Models.Recipe; // Models (Request/Response) are in Orch.Models
using Nom.Orch.Interfaces; // Interface is in Orch.Interfaces
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/Admin/[controller]")]
    // [Authorize(Roles = "Admin")]
    public class RecipesController : ControllerBase
    {
        private readonly IRecipeOrchestrationService _recipeOrchestrationService;
        private readonly ILogger<RecipesController> _logger;

        public RecipesController(IRecipeOrchestrationService recipeOrchestrationService, ILogger<RecipesController> logger)
        {
            _recipeOrchestrationService = recipeOrchestrationService;
            _logger = logger;
        }

        /// <summary>
        /// Triggers an asynchronous import of recipes from a specified CSV file.
        /// Requires Admin role.
        /// </summary>
        /// <param name="request">The request containing the source file path.</param>
        /// <returns>An API response indicating the initiation status.</returns>
        [HttpPost("import")]
        [ProducesResponseType(typeof(RecipeImportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ImportRecipes([FromBody] RecipeImportRequest request)
        {
            _logger.LogInformation("Admin user requested recipe import from: {FilePath}", request.SourceFilePath);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid model state for recipe import request.");
                return BadRequest(ModelState);
            }

            var response = await _recipeOrchestrationService.StartRecipeImportAsync(request);

            if (response.Success)
            {
                return Ok(response);
            }
            else
            {
                return BadRequest(response);
            }
        }
    }
}
