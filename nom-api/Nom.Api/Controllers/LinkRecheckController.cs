using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nom.Api.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// The re-check of links already made (plan C3), beside the food catalog's other repair routes:
    /// withdraw every waiting model pick, run a rule set over applied model links, trusted aliases,
    /// alcohol lines, cans and prepared foods, take a run back by its stamp, check catalog names,
    /// and audit a set's moves. Every write is a dry run unless apply=true. Curation-gated.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/FoodCatalog/link-recheck")]
    public class LinkRecheckController : BaseApiController
    {
        private readonly ILinkRecheckService _recheck;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _http;
        private readonly LaneModelRoster _roster;

        public LinkRecheckController(ILinkRecheckService recheck, IConfiguration configuration, IHttpClientFactory http, IOptions<LaneModelRoster> roster)
        {
            _recheck = recheck;
            _configuration = configuration;
            _http = http;
            _roster = roster.Value;
        }

        /// <summary>The rule sets, the newest run of each, the last withdrawal and how many model picks wait.</summary>
        [HttpGet("status")]
        public async Task<ActionResult<LinkRecheckStatusModel>> Status() =>
            Ok(await _recheck.StatusAsync(LinkRecheckHostedService.Enabled(_configuration), HttpContext.RequestAborted));

        /// <summary>Withdraw every pending model link pick no person reviewed, one journal entry each under one stamp; a dry run unless apply=true.</summary>
        [HttpPost("withdraw-picks")]
        public async Task<ActionResult<LinkRecheckWithdrawResult>> WithdrawPicks([FromQuery] bool apply = false, [FromQuery] int sampleSize = 50) =>
            Ok(await _recheck.WithdrawPicksAsync(apply, sampleSize, HttpContext.RequestAborted));

        /// <summary>Run one rule set over at most max wordings, most lines first; a dry run unless apply=true (the wordings set never writes).</summary>
        [HttpPost("run/{set}")]
        public async Task<ActionResult<LinkRecheckResult>> Run(string set, [FromQuery] bool apply = false, [FromQuery] int max = 2000, [FromQuery] int sampleSize = 50)
        {
            var result = await _recheck.RunAsync(set, apply, max, sampleSize, HttpContext.RequestAborted);
            return result == null ? BadRequest(new { error = $"Unknown set '{set}'.", sets = LinkRecheckRules.AllSets }) : Ok(result);
        }

        /// <summary>Take back one run (a set, a withdrawal or a rename) by its stamp; a dry run unless apply=true.</summary>
        [HttpPost("undo")]
        public async Task<ActionResult<LinkRecheckUndoResult>> Undo([FromQuery, Required] DateTime stamp, [FromQuery] bool apply = false)
        {
            var result = await _recheck.UndoAsync(DateTime.SpecifyKind(stamp.ToUniversalTime(), DateTimeKind.Utc), apply, HttpContext.RequestAborted);
            return result.Found ? Ok(result) : NotFound(new { error = $"No link re-check run has the stamp {stamp:O}." });
        }

        /// <summary>List curated foods whose name their USDA entry contradicts and rename those the fixed table names; a dry run unless apply=true.</summary>
        [HttpPost("names")]
        public async Task<ActionResult<LinkRecheckNameResult>> Names([FromQuery] bool apply = false) =>
            Ok(await _recheck.CheckNamesAsync(apply, HttpContext.RequestAborted));

        /// <summary>Audit a sample of one set's recorded moves with the roster's auditor, confirmed by reader B, and evaluate the set on the lane frame.</summary>
        [HttpPost("audit/{set}")]
        public async Task<ActionResult<LaneQualifyVerdict>> Audit(string set)
        {
            if (!LinkRecheckRules.Sets.Contains(set)) return BadRequest(new { error = $"Unknown set '{set}'.", sets = LinkRecheckRules.Sets });
            var (auditor, check, problem) = LinkRecheckHostedService.Auditors(_configuration, _roster, _http.CreateClient(LinkRecheckHostedService.HttpClientName));
            if (problem != null) return BadRequest(new { error = problem });
            return Ok(await _recheck.AuditAndEvaluateAsync(set, Array.Empty<string>(), auditor, check, HttpContext.RequestAborted));
        }
    }
}
