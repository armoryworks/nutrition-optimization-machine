using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Promotion of operator-reviewed staged recipes (the CSV/JSONL staging
    /// lane) into the catalog. Admin-only; batches are idempotent (URL or
    /// name+attribution dedup) and everything still flows through vetting and
    /// the curation queue before it can publish.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CanManageCuration")]
    public class StagedImportController : BaseApiController
    {
        private readonly IRecipeScrapingService _scraping;

        public StagedImportController(IRecipeScrapingService scraping)
        {
            _scraping = scraping;
        }

        [HttpPost]
        public async Task<ActionResult<StagedImportResultModel>> Import([FromBody] StagedImportRequestModel request)
        {
            if (request.Recipes.Count == 0)
            {
                return BadRequest(new { message = "recipes is empty" });
            }

            if (request.Recipes.Count > 500)
            {
                return BadRequest(new { message = "at most 500 recipes per batch" });
            }

            return Ok(await _scraping.ImportStagedAsync(request));
        }
    }
}
