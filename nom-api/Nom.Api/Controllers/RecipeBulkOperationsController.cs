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
            try
            {
                _logger.LogInformation("Starting bulk export for {Count} recipes", request.RecipeIds.Count);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting recipes");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Import recipes from file
        /// </summary>
        [HttpPost("import")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> ImportRecipes([FromForm] RecipeBulkImportModel request)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing recipes from file: {FileName}", request.File.FileName);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Assign categories to recipes
        /// </summary>
        [HttpPost("assign-categories")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> AssignCategories([FromBody] RecipeBulkAssignCategoriesModel request)
        {
            try
            {
                _logger.LogInformation("Assigning {CategoryCount} categories to {RecipeCount} recipes", 
                    request.Categories.Count, request.RecipeIds.Count);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning categories to recipes");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Assign tags to recipes
        /// </summary>
        [HttpPost("assign-tags")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> AssignTags([FromBody] RecipeBulkAssignTagsModel request)
        {
            try
            {
                _logger.LogInformation("Assigning {TagCount} tags to {RecipeCount} recipes", 
                    request.Tags.Count, request.RecipeIds.Count);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning tags to recipes");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Update settings for recipes
        /// </summary>
        [HttpPost("update-settings")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> UpdateSettings([FromBody] RecipeBulkUpdateSettingsModel request)
        {
            try
            {
                _logger.LogInformation("Updating settings for {RecipeCount} recipes", request.RecipeIds.Count);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating recipe settings");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete recipes
        /// </summary>
        [HttpPost("delete")]
        public async Task<ActionResult<RecipeBulkOperationResponseModel>> DeleteRecipes([FromBody] RecipeBulkDeleteModel request)
        {
            try
            {
                _logger.LogInformation("Deleting {RecipeCount} recipes (Permanent={Permanent})", 
                    request.RecipeIds.Count, request.Permanent);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting recipes");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get bulk operation progress
        /// </summary>
        [HttpGet("progress/{operationId}")]
        public async Task<ActionResult<RecipeBulkOperationProgressModel>> GetOperationProgress(long operationId)
        {
            try
            {
                var progress = await _bulkOperationsService.GetOperationProgressAsync(operationId);
                if (progress == null)
                {
                    return NotFound(new { message = "Operation not found" });
                }
                return Ok(progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting operation progress: {OperationId}", operationId);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all export files for the current user
        /// </summary>
        [HttpGet("exports")]
        public async Task<ActionResult<List<RecipeExportFileModel>>> GetExportFiles()
        {
            try
            {
                var files = await _bulkOperationsService.GetExportFilesAsync();
                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting export files");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get export file by ID
        /// </summary>
        [HttpGet("exports/{exportId}")]
        public async Task<ActionResult<RecipeExportFileModel>> GetExportFile(long exportId)
        {
            try
            {
                var file = await _bulkOperationsService.GetExportFileAsync(exportId);
                if (file == null)
                {
                    return NotFound(new { message = "Export file not found" });
                }
                return Ok(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting export file: {ExportId}", exportId);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Download export file
        /// </summary>
        [HttpGet("download/{exportId}")]
        public async Task<IActionResult> DownloadExportFile(long exportId)
        {
            try
            {
                var file = await _bulkOperationsService.GetExportFileAsync(exportId);
                if (file == null)
                {
                    return NotFound(new { message = "Export file not found" });
                }

                if (!File.Exists(file.FilePath))
                {
                    return NotFound(new { message = "Export file not found on disk" });
                }

                var fileBytes = await File.ReadAllBytesAsync(file.FilePath);
                return File(fileBytes, file.ContentType, file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading export file: {ExportId}", exportId);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete export file
        /// </summary>
        [HttpDelete("exports/{exportId}")]
        public async Task<ActionResult> DeleteExportFile(long exportId)
        {
            try
            {
                var success = await _bulkOperationsService.DeleteExportFileAsync(exportId);
                if (!success)
                {
                    return NotFound(new { message = "Export file not found" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting export file: {ExportId}", exportId);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Clean up expired export files
        /// </summary>
        [HttpPost("cleanup-exports")]
        public async Task<ActionResult<int>> CleanupExpiredExports()
        {
            try
            {
                var deletedCount = await _bulkOperationsService.CleanupExpiredExportsAsync();
                return Ok(new { deletedCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired exports");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }
    }
} 