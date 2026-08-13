using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RecipeBulkOperationsController : BaseApiController
    {
        private readonly IRecipeBulkOperationsService _bulkOperationsService;
        private readonly ILogger<RecipeBulkOperationsController> _logger;

        public RecipeBulkOperationsController(
            IRecipeBulkOperationsService bulkOperationsService,
            ILogger<RecipeBulkOperationsController> logger)
        {
            _bulkOperationsService = bulkOperationsService;
            _logger = logger;
        }

        /// <summary>
        /// Export recipes to file
        /// </summary>
        [HttpPost("export")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> ExportRecipes([FromBody] RecipeBulkExportModel request)
        {
            _logger.LogInformation("Starting bulk export for {Count} recipes", request.RecipeIds.Count);
            request.RequesterPersonId = GetCurrentPersonId();
            var result = await _bulkOperationsService.ExportRecipesAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }

        /// <summary>
        /// Import recipes from file
        /// </summary>
        [HttpPost("import")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> ImportRecipes([FromForm] RecipeBulkImportModel request)
        {
            _logger.LogInformation("Starting bulk import from file: {FileName}", request.File.FileName);
            var result = await _bulkOperationsService.ImportRecipesAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }

        /// <summary>
        /// Assign categories to recipes
        /// </summary>
        [HttpPost("assign-categories")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> AssignCategories([FromBody] RecipeBulkAssignCategoriesModel request)
        {
            _logger.LogInformation("Assigning {CategoryCount} categories to {RecipeCount} recipes",
                request.Categories.Count, request.RecipeIds.Count);
            request.RequesterPersonId = GetCurrentPersonId();
            var result = await _bulkOperationsService.AssignCategoriesAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }

        /// <summary>
        /// Assign tags to recipes
        /// </summary>
        [HttpPost("assign-tags")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> AssignTags([FromBody] RecipeBulkAssignTagsModel request)
        {
            _logger.LogInformation("Assigning {TagCount} tags to {RecipeCount} recipes",
                request.Tags.Count, request.RecipeIds.Count);
            request.RequesterPersonId = GetCurrentPersonId();
            var result = await _bulkOperationsService.AssignTagsAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }

        /// <summary>
        /// Update settings for recipes
        /// </summary>
        [HttpPost("update-settings")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> UpdateSettings([FromBody] RecipeBulkUpdateSettingsModel request)
        {
            _logger.LogInformation("Updating settings for {RecipeCount} recipes", request.RecipeIds.Count);
            request.RequesterPersonId = GetCurrentPersonId();
            var result = await _bulkOperationsService.UpdateSettingsAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }

        /// <summary>
        /// Delete recipes
        /// </summary>
        [HttpPost("delete")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> DeleteRecipes([FromBody] RecipeBulkDeleteModel request)
        {
            _logger.LogInformation("Deleting {RecipeCount} recipes (Permanent={Permanent})",
                request.RecipeIds.Count, request.Permanent);
            request.RequesterPersonId = GetCurrentPersonId();
            var result = await _bulkOperationsService.DeleteRecipesAsync(request);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }

        /// <summary>
        /// Get bulk operation progress
        /// </summary>
        [HttpGet("progress/{operationId}")]
        public async Task<ActionResult<RecipeBulkOperationProgressModel>> GetOperationProgress(long operationId)
        {
            var progress = await _bulkOperationsService.GetOperationProgressAsync(operationId);
            if (progress == null)
            {
                return NotFound(new { message = "Operation not found" });
            }
            return Ok(progress);
        }

        /// <summary>
        /// Get all export files for the current user
        /// </summary>
        [HttpGet("exports")]
        public async Task<ActionResult<List<RecipeExportFileModel>>> GetExportFiles()
        {
            var files = await _bulkOperationsService.GetExportFilesAsync();
            return Ok(files);
        }

        /// <summary>
        /// Get export file by ID
        /// </summary>
        [HttpGet("exports/{exportId}")]
        public async Task<ActionResult<RecipeExportFileModel>> GetExportFile(long exportId)
        {
            var file = await _bulkOperationsService.GetExportFileAsync(exportId);
            if (file == null)
            {
                return NotFound(new { message = "Export file not found" });
            }
            return Ok(file);
        }

        /// <summary>
        /// Download export file
        /// </summary>
        [HttpGet("download/{exportId}")]
        public async Task<IActionResult> DownloadExportFile(long exportId)
        {
            var file = await _bulkOperationsService.GetExportFileAsync(exportId);
            if (file == null)
            {
                return NotFound(new { message = "Export file not found" });
            }

            if (!System.IO.File.Exists(file.FilePath))
            {
                return NotFound(new { message = "Export file not found on disk" });
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(file.FilePath);
            return File(fileBytes, file.ContentType, file.FileName);
        }

        /// <summary>
        /// Delete export file
        /// </summary>
        [HttpDelete("exports/{exportId}")]
        public async Task<ActionResult> DeleteExportFile(long exportId)
        {
            var success = await _bulkOperationsService.DeleteExportFileAsync(exportId);
            if (!success)
            {
                return NotFound(new { message = "Export file not found" });
            }
            return NoContent();
        }

        /// <summary>
        /// Clean up expired export files
        /// </summary>
        [HttpPost("cleanup-exports")]
        public async Task<ActionResult<int>> CleanupExpiredExports()
        {
            var deletedCount = await _bulkOperationsService.CleanupExpiredExportsAsync();
            return Ok(new { deletedCount });
        }
    }
}