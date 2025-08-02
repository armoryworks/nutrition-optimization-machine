using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RecipeImportController : BaseApiController
    {
        private readonly IRecipeImportOrchestrationService _importService;

        public RecipeImportController(IRecipeImportOrchestrationService importService)
        {
            _importService = importService;
        }

        /// <summary>
        /// Test URL scraping without creating a recipe
        /// </summary>
        [HttpPost("test-scrape-url")]
        public async Task<ActionResult<RecipeScrapeTestModel>> TestUrlScraping([FromBody] string url)
        {
            try
            {
                var result = await _importService.TestUrlScrapingAsync(url);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to test URL scraping", error = ex.Message });
            }
        }

        /// <summary>
        /// Import a recipe from a URL
        /// </summary>
        [HttpPost("create/url")]
        public async Task<ActionResult<RecipeCreateResponseModel>> ImportFromUrl([FromBody] string url)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var result = await _importService.ImportFromUrlAsync(url, authorId);
                return CreatedAtAction(nameof(ImportFromUrl), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to import recipe from URL", error = ex.Message });
            }
        }

        /// <summary>
        /// Bulk import recipes from multiple URLs
        /// </summary>
        [HttpPost("create/url/bulk")]
        public async Task<ActionResult<List<RecipeCreateResponseModel>>> BulkImportFromUrls([FromBody] List<string> urls)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var results = await _importService.BulkImportFromUrlsAsync(urls, authorId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to bulk import recipes", error = ex.Message });
            }
        }

        /// <summary>
        /// Import a recipe from an image (OCR)
        /// </summary>
        [HttpPost("create/image")]
        public async Task<ActionResult<RecipeCreateResponseModel>> ImportFromImage([FromBody] byte[] imageData)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var result = await _importService.ImportFromImageAsync(imageData, authorId);
                return CreatedAtAction(nameof(ImportFromImage), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to import recipe from image", error = ex.Message });
            }
        }

        /// <summary>
        /// Import a recipe from HTML or JSON data
        /// </summary>
        [HttpPost("create/html-or-json")]
        public async Task<ActionResult<RecipeCreateResponseModel>> ImportFromHtmlOrJson([FromBody] string htmlOrJson)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var result = await _importService.ImportFromHtmlOrJsonAsync(htmlOrJson, authorId);
                return CreatedAtAction(nameof(ImportFromHtmlOrJson), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to import recipe from HTML/JSON", error = ex.Message });
            }
        }

        /// <summary>
        /// Import recipes from a ZIP archive
        /// </summary>
        [HttpPost("create/zip")]
        public async Task<ActionResult<List<RecipeCreateResponseModel>>> ImportFromZip([FromBody] byte[] zipData)
        {
            try
            {
                var authorId = GetCurrentPersonId();
                var results = await _importService.ImportFromZipAsync(zipData, authorId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to import recipes from ZIP", error = ex.Message });
            }
        }
    }
} 