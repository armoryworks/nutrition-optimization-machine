using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin management of the scraping-source whitelist. Scraping is
    /// deny-by-default; approving a domain here is the admin accepting
    /// responsibility for the legality and quality of importing from it.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CanManageCuration")]
    public class ScrapingSourceController : BaseApiController
    {
        private readonly IScrapingSourceService _scrapingSources;
        private readonly ICurrentUserService _currentUser;

        public ScrapingSourceController(
            IScrapingSourceService scrapingSources,
            ICurrentUserService currentUser)
        {
            _scrapingSources = scrapingSources;
            _currentUser = currentUser;
        }

        /// <summary>List scraping sources, optionally filtered by status (Pending/Approved/Rejected).</summary>
        [HttpGet]
        public async Task<ActionResult<List<ScrapingSourceModel>>> List([FromQuery] ScrapingSourceStatusEnum? status)
        {
            return Ok(await _scrapingSources.ListAsync(status));
        }

        [HttpPost("{id:long}/approve")]
        public async Task<ActionResult<ScrapingSourceModel>> Approve(long id, [FromBody] ScrapingSourceReviewRequestModel? request)
        {
            var result = await _scrapingSources.ApproveAsync(id, _currentUser.RequiredPersonId, request?.Notes);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost("{id:long}/reject")]
        public async Task<ActionResult<ScrapingSourceModel>> Reject(long id, [FromBody] ScrapingSourceReviewRequestModel? request)
        {
            var result = await _scrapingSources.RejectAsync(id, _currentUser.RequiredPersonId, request?.Notes);
            return result == null ? NotFound() : Ok(result);
        }
    }
}
