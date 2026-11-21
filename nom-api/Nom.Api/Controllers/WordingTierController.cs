using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Services;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// The operator's entry point to the deterministic wording tiers, which otherwise run only in
    /// their hosted lane behind the pause switches. Neither call asks a model. Curation-gated.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/FoodCatalog/wording-tiers")]
    public class WordingTierController : BaseApiController
    {
        public const int MaxPreview = 20000;
        public const int MaxPass = 1000;

        private readonly IWordingTierLaneService _tiers;
        private readonly AiLaneGate _gate;
        private readonly IConfiguration _configuration;

        public WordingTierController(IWordingTierLaneService tiers, AiLaneGate gate, IConfiguration configuration)
        {
            _tiers = tiers;
            _gate = gate;
            _configuration = configuration;
        }

        /// <summary>
        /// Decide at most max waiting wordings (most lines first, after skip) by the tiers and the gate and
        /// show the coverage of each tier, the holds, the loader's refusals, the abstentions and samples.
        /// Records nothing and writes nothing.
        /// </summary>
        [HttpPost("preview")]
        public async Task<ActionResult<WordingTierPreview>> Preview([FromQuery] int skip = 0, [FromQuery] int max = 1000, [FromQuery] int sampleSize = 20, CancellationToken cancellationToken = default)
        {
            if (skip < 0 || max < 1 || max > MaxPreview)
            {
                return BadRequest(new { message = $"skip must be 0 or more and max 1 to {MaxPreview}" });
            }

            return Ok(await _tiers.PreviewAsync(skip, max, Math.Clamp(sampleSize, 0, 200), cancellationToken));
        }

        /// <summary>
        /// Run one pass of the lane over at most max wordings, as the hosted lane does but without its
        /// model audit: every decision is recorded on the lane frame in the mode the lane would write in
        /// (paused while a pause switch holds the lane, else the tier's own mode), so a shadow sample
        /// builds up for the audit and the operator's reading. Only while WordingTiers:Enabled is true
        /// and no pause switch holds the lane does a tier the frame holds live write rows, within its
        /// daily cap, does the pass recover rows a tier loaded but never recorded, and are the pass's
        /// faults evaluated on the frame (no new audit), so a fault pauses the tier and undoes its
        /// writes as in the lane. Otherwise a live tier decides in shadow and nothing is written.
        /// </summary>
        [HttpPost("run")]
        public async Task<ActionResult<WordingTierBatchResult>> Run([FromQuery] int max = 100, CancellationToken cancellationToken = default)
        {
            if (max < 1 || max > MaxPass)
            {
                return BadRequest(new { message = $"max must be 1 to {MaxPass}" });
            }

            var acts = WordingTierHostedService.Enabled(_configuration) && !await _gate.PausedAsync(AiLanes.WordingTiers);
            var caps = WordingTierHostedService.Caps(_configuration);
            var modes = new Dictionary<string, string>();
            var remaining = new Dictionary<string, int>();
            foreach (var tier in WordingTiers.All)
            {
                var mode = await _gate.WriteModeAsync(AiLanes.WordingTiers, tier, cancellationToken);
                modes[tier] = !acts && mode == LaneMode.Live ? LaneMode.Shadow : mode;
                remaining[tier] = Math.Max(0, caps[tier] - await _tiers.WritesTodayAsync(tier, cancellationToken));
            }

            var batch = await _tiers.RunBatchAsync(modes, max, remaining, recover: acts, cancellationToken: cancellationToken);
            if (!acts) return Ok(batch);

            foreach (var (tier, faults) in batch.FaultsByTier.Where(f => f.Value.Count > 0 && WordingTiers.All.Contains(f.Key)))
            {
                var verdict = await _tiers.EvaluateFaultsAsync(tier, faults, cancellationToken);
                batch.Evaluated[tier] = $"{verdict.Mode}: {verdict.Reason}";
            }
            return Ok(batch);
        }
    }
}
