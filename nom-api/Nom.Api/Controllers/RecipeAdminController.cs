// Nom.Api/Controllers/RecipeAdminController.cs
using Nom.Orch.Models.Recipe; // For RecipeImportRequest, RecipeImportResponse
using Nom.Orch.Models.Audit; // For ImportJobStatusResponse
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Nom.Orch.UtilityInterfaces; // For IKaggleRecipeIngestionService
using System; // For Guid

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/Admin/[controller]")]
    // [Authorize(Roles = "Admin")] // Uncomment this line once authentication and authorization are set up
    public class RecipesController : ControllerBase
    {
        private readonly IKaggleRecipeIngestionService _kaggleRecipeIngestionService;
        private readonly ILogger<RecipesController> _logger;

        public RecipesController(IKaggleRecipeIngestionService kaggleRecipeIngestionService, ILogger<RecipesController> logger)
        {
            _kaggleRecipeIngestionService = kaggleRecipeIngestionService;
            _logger = logger;
        }

        /// <summary>
        /// Triggers an asynchronous import of recipes from a specified Kaggle CSV file.
        /// This is the public API endpoint for starting the import process.
        /// Requires Admin role.
        /// </summary>
        /// <param name="request">The request containing the source file path.</param>
        /// <returns>An API response indicating the initiation status.</returns>
        [HttpPost("import")] // POST /api/Admin/Recipes/import
        [ProducesResponseType(typeof(RecipeImportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ImportRecipes([FromBody] RecipeImportRequest request)
        {
            _logger.LogInformation("Admin user requested Kaggle recipe import from: {FilePath}", request.SourceFilePath);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid model state for Kaggle recipe import request.");
                return BadRequest(ModelState);
            }

            // Delegate the import initiation directly to the KaggleRecipeIngestionService
            var response = await _kaggleRecipeIngestionService.StartRecipeImportAsync(request);

            if (response.Success)
            {
                return Ok(response);
            }
            else
            {
                return BadRequest(response);
            }
        }

        /// <summary>
        /// Retrieves the current status of a specific asynchronous recipe import job.
        /// Requires Admin role.
        /// </summary>
        /// <param name="processId">The unique identifier (GUID) of the import job to query.</param>
        /// <returns>An API response containing the job's status and metrics, or NotFound if the job does not exist.</returns>
        [HttpGet("import/{processId}/status")] // GET /api/Admin/Recipes/import/{processId}/status
        [ProducesResponseType(typeof(ImportJobStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetImportStatus([FromRoute] Guid processId)
        {
            _logger.LogInformation("Admin user requested status for import job: {ProcessId}", processId);

            if (processId == Guid.Empty)
            {
                _logger.LogWarning("Attempted to query import status with empty ProcessId.");
                return BadRequest("Process ID cannot be empty.");
            }

            var statusResponse = await _kaggleRecipeIngestionService.GetImportStatusAsync(processId);

            if (statusResponse == null)
            {
                _logger.LogInformation("Import job with ProcessId {ProcessId} not found.", processId);
                return NotFound($"Import job with ID '{processId}' not found.");
            }

            return Ok(statusResponse);
        }
    }
}
