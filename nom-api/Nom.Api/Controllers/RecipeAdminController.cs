// Nom.Api/Controllers/RecipeAdminController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.UtilityInterfaces; // For IKaggleRecipeIngestionService
using Nom.Orch.Models.Recipe; // For RecipeImportRequest, RecipeImportResponse, RecipeImportFromFileRequestModel
using Nom.Orch.Models.Audit; // For ImportJobStatusResponse
using System;
using System.IO; // Required for Path.Combine, FileMode, FileStream
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// API controller for administrative tasks related to recipe management,
    /// specifically for initiating and monitoring recipe data imports.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")] // Base route: /api/RecipeAdmin
    // [Authorize(Roles = "Admin")] // Uncomment and implement role-based authorization when ready
    public class RecipeAdminController : ControllerBase
    {
        private readonly ILogger<RecipeAdminController> _logger;
        private readonly IKaggleRecipeIngestionService _kaggleRecipeIngestionService;

        public RecipeAdminController(ILogger<RecipeAdminController> logger,
                                     IKaggleRecipeIngestionService kaggleRecipeIngestionService)
        {
            _logger = logger;
            _kaggleRecipeIngestionService = kaggleRecipeIngestionService;
        }

        /// <summary>
        /// Initiates a recipe import job from a specified CSV file path (server-side path).
        /// This operation is asynchronous and returns a job ID immediately.
        /// </summary>
        /// <param name="request">The <see cref="RecipeImportRequest"/> containing the source file path.</param>
        /// <returns>A <see cref="RecipeImportResponse"/> with the job's process ID.</returns>
        [HttpPost("Recipes/import")] // Endpoint: /api/RecipeAdmin/Recipes/import
        [ProducesResponseType(typeof(RecipeImportResponse), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ImportRecipes([FromBody] RecipeImportRequest request)
        {
            // Validate the incoming request model based on its data annotations (e.g., [Required])
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid model state for recipe import request.");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Received request to import recipes from: {FilePath}", request.SourceFilePath);

            // Delegate the initiation of the import process to the orchestration service.
            // This method is designed to return quickly, launching the long-running process in the background.
            var response = await _kaggleRecipeIngestionService.StartRecipeImportAsync(request);

            // Check if the initiation itself was successful (e.g., file existence check in service)
            if (!response.Success)
            {
                _logger.LogError("Failed to initiate recipe import for '{FilePath}': {Message}", request.SourceFilePath, response.Message);
                return BadRequest(response); // Return error message if initiation failed
            }

            // Return a 200 OK with the ProcessId for the client to track the job status.
            _logger.LogInformation("Recipe import job successfully initiated. Process ID: {ProcessId}", response.ProcessId);
            return Ok(response);
        }

        /// <summary>
        /// Initiates a recipe import job by receiving a file upload from the client.
        /// The file is saved temporarily, and then the import process is started using its path.
        /// </summary>
        /// <param name="request">The <see cref="RecipeImportFromFileRequestModel"/> containing the uploaded file and job name.</param>
        /// <returns>A <see cref="RecipeImportResponse"/> with the job's process ID.</returns>
        [HttpPost("Recipes/import-from-file")] // Endpoint: /api/RecipeAdmin/Recipes/import-from-file
        [Consumes("multipart/form-data")] // Explicitly state the content type for Swagger
        [ProducesResponseType(typeof(RecipeImportResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        [RequestSizeLimit(524288000)] // Increased: Allows up to 500 MB (500 * 1024 * 1024 bytes)
        public async Task<ActionResult<RecipeImportResponse>> ImportRecipesFromFile([FromForm] RecipeImportFromFileRequestModel request)
        {
            // ModelState.IsValid will now check if File and JobName are provided
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid model state for recipe import from file request.");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Received file '{FileName}' for import with job name: '{JobName}'", request.File.FileName, request.JobName);

            // Generate a unique file path for temporary storage
            var tempFileName = $"{Guid.NewGuid()}_{request.File.FileName}";
            var filePath = Path.Combine(Path.GetTempPath(), tempFileName);

            try
            {
                // Save the uploaded file temporarily
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }
                _logger.LogInformation("File saved temporarily to: {FilePath}", filePath);

                // Now initiate your ingestion service using the saved file path and the provided jobName
                var importRequest = new RecipeImportRequest
                {
                    SourceFilePath = filePath,
                    JobName = request.JobName // Pass the job name to the service
                };

                // Delegate the initiation of the import process to the orchestration service.
                var response = await _kaggleRecipeIngestionService.StartRecipeImportAsync(importRequest);

                // Check if the initiation itself was successful
                if (!response.Success)
                {
                    _logger.LogError("Failed to initiate recipe import for uploaded file '{FileName}': {Message}", request.File.FileName, response.Message);
                    // Optionally clean up the temp file if initiation failed
                    System.IO.File.Delete(filePath);
                    return BadRequest(response);
                }

                _logger.LogInformation("Recipe import job for file '{FileName}' successfully initiated. Process ID: {ProcessId}", request.File.FileName, response.ProcessId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing uploaded file '{FileName}' for import.", request.File.FileName);
                return StatusCode(500, new RecipeImportResponse { Success = false, Message = $"An error occurred during file processing: {ex.Message}" });
            }
        }

        /// <summary>
        /// Retrieves the current status of a specific recipe import job.
        /// </summary>
        /// <param name="processId">The unique GUID of the import job to query.</param>
        /// <returns>The <see cref="ImportJobStatusResponse"/> of the job, or 404 if not found.</returns>
        [HttpGet("Recipes/import/{processId}/status")] // Endpoint: /api/RecipeAdmin/Recipes/import/{processId}/status
        [ProducesResponseType(typeof(ImportJobStatusResponse), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetImportStatus([FromRoute] Guid processId)
        {
            _logger.LogInformation("Received request for import job status for Process ID: {ProcessId}", processId);

            // Delegate the status retrieval to the orchestration service.
            var status = await _kaggleRecipeIngestionService.GetImportStatusAsync(processId);

            // If the job with the given ProcessId is not found, return 404 Not Found.
            if (status == null)
            {
                _logger.LogWarning("Import job with Process ID '{ProcessId}' not found.", processId);
                return NotFound($"Import job with Process ID '{processId}' not found.");
            }

            // Return a 200 OK with the detailed status of the import job.
            _logger.LogInformation("Returned status for import job {ProcessId}. Current Status: {Status}", processId, status.Status);
            return Ok(status);
        }
    }
}
