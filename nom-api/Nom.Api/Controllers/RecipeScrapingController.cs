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
            _logger.LogInformation("Testing recipe scraping from URL: {Url}", request.Url);
            var result = await _recipeScrapingService.TestScrapeRecipeAsync(request);
            return Ok(result);
        }

        /// <summary>
        /// Scrape recipe from HTML or JSON data
        /// </summary>
        [HttpPost("create/html-or-json")]
        public async Task<ActionResult<RecipeScrapingResponseModel>> CreateRecipeFromData([FromBody] RecipeScrapingDataRequestModel request)
        {
            _logger.LogInformation("Creating recipe from HTML or JSON data");
            var result = await _recipeScrapingService.ScrapeRecipeFromDataAsync(request);

            if (result.Success)
            {
                return CreatedAtAction("GetRecipe", "Recipe", new { id = result.RecipeId }, result);
            }
            else
            {
                return BadRequest(new { message = "Failed to create recipe", error = result.Error });
            }
        }

        /// <summary>
        /// Scrape recipe from URL
        /// </summary>
        [HttpPost("create/url")]
        public async Task<ActionResult<RecipeScrapingResponseModel>> CreateRecipeFromUrl([FromBody] RecipeScrapingRequestModel request)
        {
            _logger.LogInformation("Creating recipe from URL: {Url}", request.Url);
            var result = await _recipeScrapingService.ScrapeRecipeFromUrlAsync(request);

            if (result.Success)
            {
                return CreatedAtAction("GetRecipe", "Recipe", new { id = result.RecipeId }, result);
            }
            else
            {
                return BadRequest(new { message = "Failed to create recipe", error = result.Error });
            }
        }

        /// <summary>
        /// Bulk scrape recipes from multiple URLs
        /// </summary>
        [HttpPost("bulk-scrape")]
        public async Task<ActionResult<RecipeBulkScrapingResponseModel>> BulkScrapeRecipes([FromBody] RecipeBulkScrapingRequestModel request)
        {
            _logger.LogInformation("Bulk scraping {Count} recipes", request.Imports.Count);
            var result = await _recipeScrapingService.BulkScrapeRecipesAsync(request);
            return Ok(result);
        }

        /// <summary>
        /// Get scraping report by ID
        /// </summary>
        [HttpGet("reports/{reportId}")]
        public async Task<ActionResult<RecipeBulkScrapingResponseModel>> GetScrapingReport(long reportId)
        {
            var report = await _recipeScrapingService.GetScrapingReportAsync(reportId);
            if (report == null)
            {
                return NotFound(new { message = "Scraping report not found" });
            }
            return Ok(report);
        }

        /// <summary>
        /// Get all scraping reports for the current user
        /// </summary>
        [HttpGet("reports")]
        public async Task<ActionResult<List<RecipeBulkScrapingResponseModel>>> GetScrapingReports()
        {
            var reports = await _recipeScrapingService.GetScrapingReportsAsync();
            return Ok(reports);
        }

    }
}