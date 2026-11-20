using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin view of the automated catalog pipeline: where recipes, lines and foods are waiting,
    /// each lane's settings and queue, and the model lanes' live state from the lane gate. Read-only
    /// except for the AI pause switches: a curation admin may turn a pause on, and only a user-roles
    /// admin may turn one off, as on the platform feature endpoint that writes the same switches.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class PipelineController : BaseApiController
    {
        public const string AllLanes = "all";
        public const string AutoApprove = "auto-approve";

        private readonly IPipelineStatusService _pipeline;
        private readonly IAiLaneStatus _lanes;
        private readonly IPlatformFeatureService _features;
        private readonly IConfiguration _configuration;

        public PipelineController(IPipelineStatusService pipeline, IAiLaneStatus lanes, IPlatformFeatureService features, IConfiguration configuration)
        {
            _pipeline = pipeline;
            _lanes = lanes;
            _features = features;
            _configuration = configuration;
        }

        /// <summary>The pipeline counts (cached for a few minutes; refresh=true recomputes them) and the lanes' live state.</summary>
        [HttpGet("status")]
        public async Task<ActionResult<PipelineStatusResponse>> GetStatus([FromQuery] bool refresh = false, CancellationToken cancellationToken = default)
        {
            var status = await _pipeline.GetAsync(refresh, cancellationToken);
            var left = AiQuietHours.Remaining(_configuration, DateTimeOffset.UtcNow);
            status = status with { Ai = status.Ai with { QuietMinutesLeft = left is { } l ? (int)Math.Ceiling(l.TotalMinutes) : null } };
            return Ok(new PipelineStatusResponse(status, await GateAsync()));
        }

        /// <summary>Turns the pause switch of one model lane, of all of them ("all") or of recipe auto-approval ("auto-approve") on or off; turning one off needs CanManageUserRoles.</summary>
        [HttpPut("pause/{lane}")]
        public async Task<ActionResult<PipelineGateStatus>> SetPause(string lane, [FromBody] SetPipelinePauseRequest request)
        {
            var key = string.Equals(lane, AllLanes, StringComparison.OrdinalIgnoreCase) ? AiLanes.PauseAllKey
                : string.Equals(lane, AutoApprove, StringComparison.OrdinalIgnoreCase) ? IPlatformFeatureService.Keys.AutoApprovePause
                : AiLanes.All.Any(l => string.Equals(l.Lane, lane, StringComparison.OrdinalIgnoreCase)) ? AiLanes.PauseKey(lane.ToLowerInvariant())
                : null;
            if (key == null)
                return BadRequest(new { error = $"Unknown lane '{lane}'.", lanes = AiLanes.All.Select(l => l.Lane).Prepend(AllLanes).Append(AutoApprove) });
            if (!request.IsEnabled && !User.HasClaim("CanManageUserRoles", "true"))
                return Forbid();

            await _features.SetAsync(key, request.IsEnabled, GetCurrentPersonIdRequired());
            return Ok(await GateAsync());
        }

        private async Task<PipelineGateStatus> GateAsync()
        {
            var states = _lanes.GetLanes().ToDictionary(l => l.Lane, StringComparer.OrdinalIgnoreCase);
            var lanes = new List<PipelineGateLane>();
            foreach (var (lane, label) in AiLanes.All)
            {
                lanes.Add(new PipelineGateLane(lane, label, await _features.IsEnabledAsync(AiLanes.PauseKey(lane)),
                    states.GetValueOrDefault(lane) ?? new AiLaneStatusModel { Lane = lane }));
            }
            return new PipelineGateStatus(await _features.IsEnabledAsync(AiLanes.PauseAllKey), lanes, _lanes.GetHosts(),
                await _features.IsEnabledAsync(IPlatformFeatureService.Keys.AutoApprovePause));
        }
    }
}
