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

        private readonly Nom.Orch.Interfaces.IPolicyEnforcementService _policyService;

        public RecipeImportController(
            IRecipeImportOrchestrationService importService,
            Nom.Orch.Interfaces.IPolicyEnforcementService policyService)
        {
            _importService = importService;
            _policyService = policyService;
        }

        /// <summary>Non-null when the caller's household policy gates recipe import.</summary>
        private async Task<ActionResult?> CheckImportGateAsync()
        {
            var personId = GetCurrentPersonId();
            if (personId.HasValue
                && await _policyService.IsFeatureGatedAnywhereAsync(personId.Value, Nom.Orch.Services.FeatureGateKeys.RecipeImport))
            {
                return StatusCode(403, new { message = "Recipe import is disabled by your household policy.", reason = "feature_gated:recipe_import" });
            }
            return null;
        }

        /// <summary>
        /// Test URL scraping without creating a recipe
        /// </summary>
        [HttpPost("test-scrape-url")]
        public async Task<ActionResult<RecipeScrapeTestModel>> TestUrlScraping([FromBody] string url)
        {
            var result = await _importService.TestUrlScrapingAsync(url);
            return Ok(result);
        }

        /// <summary>
        /// Import a recipe from a URL
        /// </summary>
        [HttpPost("create/url")]
        public async Task<ActionResult<RecipeCreateResponseModel>> ImportFromUrl([FromBody] string url)
        {
            var gate = await CheckImportGateAsync();
            if (gate != null) return gate;

            var authorId = GetCurrentPersonIdRequired();
            var result = await _importService.ImportFromUrlAsync(url, authorId);
            return CreatedAtAction(nameof(ImportFromUrl), new { id = result.Id }, result);
        }

        /// <summary>
        /// Bulk import recipes from multiple URLs
        /// </summary>
        [HttpPost("create/url/bulk")]
        public async Task<ActionResult<List<RecipeCreateResponseModel>>> BulkImportFromUrls([FromBody] List<string> urls)
        {
            var gate = await CheckImportGateAsync();
            if (gate != null) return gate;

            var authorId = GetCurrentPersonIdRequired();
            var results = await _importService.BulkImportFromUrlsAsync(urls, authorId);
            return Ok(results);
        }

        /// <summary>
        /// Import a recipe from an image (OCR)
        /// </summary>
        [HttpPost("create/image")]
        public async Task<ActionResult<RecipeCreateResponseModel>> ImportFromImage([FromBody] byte[] imageData)
        {
            var gate = await CheckImportGateAsync();
            if (gate != null) return gate;

            var authorId = GetCurrentPersonIdRequired();
            var result = await _importService.ImportFromImageAsync(imageData, authorId);
            return CreatedAtAction(nameof(ImportFromImage), new { id = result.Id }, result);
        }

        /// <summary>
        /// Import a recipe from HTML or JSON data
        /// </summary>
        [HttpPost("create/html-or-json")]
        public async Task<ActionResult<RecipeCreateResponseModel>> ImportFromHtmlOrJson([FromBody] string htmlOrJson)
        {
            var gate = await CheckImportGateAsync();
            if (gate != null) return gate;

            var authorId = GetCurrentPersonIdRequired();
            var result = await _importService.ImportFromHtmlOrJsonAsync(htmlOrJson, authorId);
            return CreatedAtAction(nameof(ImportFromHtmlOrJson), new { id = result.Id }, result);
        }

        /// <summary>
        /// Import recipes from a ZIP archive
        /// </summary>
        [HttpPost("create/zip")]
        public async Task<ActionResult<List<RecipeCreateResponseModel>>> ImportFromZip([FromBody] byte[] zipData)
        {
            var gate = await CheckImportGateAsync();
            if (gate != null) return gate;

            var authorId = GetCurrentPersonIdRequired();
            var results = await _importService.ImportFromZipAsync(zipData, authorId);
            return Ok(results);
        }
    }
}