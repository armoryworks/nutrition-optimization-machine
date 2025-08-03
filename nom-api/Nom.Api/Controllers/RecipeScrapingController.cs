using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RecipeScrapingController : BaseApiController
    {
        private readonly IRecipeScrapingService _recipeScrapingService;
        private readonly ILogger<RecipeScrapingController> _logger;

        public RecipeScrapingController(
            IRecipeScrapingService recipeScrapingService,
            ILogger<RecipeScrapingController> logger)
        {
            _recipeScrapingService = recipeScrapingService;
            _logger = logger;
        }

        /// <summary>
        /// Test recipe scraping from a URL
        /// </summary>
        [HttpPost("test-scrape-url")]
        public async Task<ActionResult<ScrapedRecipeModel>> TestScrapeRecipe([FromBody] RecipeScrapingTestRequestModel request)
        {
            try
            {
                _logger.LogInformation("Testing recipe scraping from URL: {Url}", request.Url);
                var result = await _recipeScrapingService.TestScrapeRecipeAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = "Invalid URL format", error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = "Failed to scrape recipe", error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing recipe scraping from URL: {Url}", request.Url);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Scrape recipe from HTML or JSON data
        /// </summary>
        [HttpPost("create/html-or-json")]
        public async Task<ActionResult<RecipeScrapingResponseModel>> CreateRecipeFromData([FromBody] RecipeScrapingDataRequestModel request)
        {
            try
            {
                _logger.LogInformation("Creating recipe from HTML or JSON data");
                var result = await _recipeScrapingService.ScrapeRecipeFromDataAsync(request);
                
                if (result.Success)
                {
                    return CreatedAtAction(nameof(GetRecipe), new { id = result.RecipeId }, result);
                }
                else
                {
                    return BadRequest(new { message = "Failed to create recipe", error = result.Error });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating recipe from data");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Scrape recipe from URL
        /// </summary>
        [HttpPost("create/url")]
        public async Task<ActionResult<RecipeScrapingResponseModel>> CreateRecipeFromUrl([FromBody] RecipeScrapingRequestModel request)
        {
            try
            {
                _logger.LogInformation("Creating recipe from URL: {Url}", request.Url);
                var result = await _recipeScrapingService.ScrapeRecipeFromUrlAsync(request);
                
                if (result.Success)
                {
                    return CreatedAtAction(nameof(GetRecipe), new { id = result.RecipeId }, result);
                }
                else
                {
                    return BadRequest(new { message = "Failed to create recipe", error = result.Error });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating recipe from URL: {Url}", request.Url);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Bulk scrape recipes from multiple URLs
        /// </summary>
        [HttpPost("bulk-scrape")]
        public async Task<ActionResult<RecipeBulkScrapingResponseModel>> BulkScrapeRecipes([FromBody] RecipeBulkScrapingRequestModel request)
        {
            try
            {
                _logger.LogInformation("Bulk scraping {Count} recipes", request.Imports.Count);
                var result = await _recipeScrapingService.BulkScrapeRecipesAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk scraping recipes");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get scraping report by ID
        /// </summary>
        [HttpGet("reports/{reportId}")]
        public async Task<ActionResult<RecipeBulkScrapingResponseModel>> GetScrapingReport(long reportId)
        {
            try
            {
                var report = await _recipeScrapingService.GetScrapingReportAsync(reportId);
                if (report == null)
                {
                    return NotFound(new { message = "Scraping report not found" });
                }
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scraping report: {ReportId}", reportId);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all scraping reports for the current user
        /// </summary>
        [HttpGet("reports")]
        public async Task<ActionResult<List<RecipeBulkScrapingResponseModel>>> GetScrapingReports()
        {
            try
            {
                var reports = await _recipeScrapingService.GetScrapingReportsAsync();
                return Ok(reports);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scraping reports");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get recipe by ID (helper method for CreatedAtAction)
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<RecipeResponseModel>> GetRecipe(long id)
        {
            // This is a placeholder for the CreatedAtAction
            // The actual implementation would be in RecipeController
            return NotFound();
        }
    }
} 