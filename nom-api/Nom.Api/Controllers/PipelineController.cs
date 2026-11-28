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
    /// Admin view of the automated catalog pipeline: where recipes, lines and foods are waiting,
    /// each lane's settings and queue, the model lanes' live state from the lane gate, and every lane
    /// on the lane frame with its mode, figures and bounds, the reprocess lane's candidates and
    /// batches, and the link re-check's runs. Read-only except for the AI pause
    /// switches and the lane frame's undo: a curation admin may turn a pause on and dry-run an undo,
    /// and only a user-roles admin may turn a pause off or apply an undo, as on the platform feature
    /// endpoint that writes the same switches.
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
        private readonly ILaneFrameService _frame;
        private readonly LaneModelRoster _roster;
        private readonly IReprocessLaneService? _reprocess;
        private readonly ILinkRecheckService? _recheck;
        private readonly IContainerSizeService? _containerSizes;
        private readonly ISourceAdmissionService? _sources;
        private readonly ISourceRightsService? _rights;
        private readonly IRecipeAutoCurationService? _autoCuration;

        public PipelineController(IPipelineStatusService pipeline, IAiLaneStatus lanes, IPlatformFeatureService features, IConfiguration configuration,
            ILaneFrameService frame, IOptions<LaneModelRoster> roster, IReprocessLaneService? reprocess = null, ILinkRecheckService? recheck = null,
            IContainerSizeService? containerSizes = null, ISourceAdmissionService? sources = null, ISourceRightsService? rights = null,
            IRecipeAutoCurationService? autoCuration = null)
        {
            _autoCuration = autoCuration;
            _sources = sources;
            _rights = rights;
            _reprocess = reprocess;
            _recheck = recheck;
            _containerSizes = containerSizes;
            _pipeline = pipeline;
            _lanes = lanes;
            _features = features;
            _configuration = configuration;
            _frame = frame;
            _roster = roster.Value;
        }

        /// <summary>The pipeline counts (cached for a few minutes; refresh=true recomputes them) and the lanes' live state.</summary>
        [HttpGet("status")]
        public async Task<ActionResult<PipelineStatusResponse>> GetStatus([FromQuery] bool refresh = false, CancellationToken cancellationToken = default)
        {
            var status = await _pipeline.GetAsync(refresh, cancellationToken);
            var left = AiQuietHours.Remaining(_configuration, DateTimeOffset.UtcNow);
            status = status with { Ai = status.Ai with { QuietMinutesLeft = left is { } l ? (int)Math.Ceiling(l.TotalMinutes) : null } };
            var reprocess = _reprocess == null ? null : await _reprocess.StatusAsync(ReprocessHostedService.Enabled(_configuration), cancellationToken);
            var recheck = _recheck == null ? null : await _recheck.StatusAsync(LinkRecheckHostedService.Enabled(_configuration), cancellationToken);
            var containerSizes = _containerSizes == null ? null : await _containerSizes.StatusAsync(20, cancellationToken);
            var sources = _sources == null ? null : await _sources.StatusAsync(cancellationToken);
            var rights = _rights == null ? null : await _rights.StatusAsync(cancellationToken);
            return Ok(new PipelineStatusResponse(status, await GateAsync(), await FrameAsync(cancellationToken), reprocess, recheck, containerSizes, sources, rights));
        }

        /// <summary>
        /// A frame lane's undo of its writes since its last clean audit window: a dry run by default;
        /// apply=true needs CanManageUserRoles. Tier "*" is the whole lane. 404 when the lane has no
        /// state or registered no undo.
        /// </summary>
        [HttpPost("frame/{lane}/{tier}/undo")]
        public async Task<ActionResult<LaneUndoReport>> UndoFrameWindow(string lane, string tier, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            if (apply && !User.HasClaim("CanManageUserRoles", "true"))
                return Forbid();
            var report = await _frame.UndoWindowAsync(lane, tier, apply, GetCurrentPersonIdRequired(), cancellationToken);
            return report == null ? NotFound(new { error = $"Lane '{lane}/{tier}' has no state or no undo." }) : Ok(report);
        }

        /// <summary>
        /// A dry run of recipe auto-approval (plan item ck23, U7): every candidate's reason under the configured options, as the
        /// sweep would decide it now, also while the sweep is paused: refused by the policy or vetting by rule, waiting for the
        /// diet check (not yet decided, held as unknown by a severe type, not yet read by the second reader), or approvable,
        /// with counts and up to <paramref name="sample"/> recipe ids each. Writes nothing.
        /// </summary>
        [HttpGet("auto-approve/waiting")]
        public async Task<ActionResult<AutoApproveWaitingReport>> AutoApproveWaiting([FromQuery] int sample = 20, CancellationToken cancellationToken = default)
        {
            if (_autoCuration == null) return NotFound();
            return Ok(await _autoCuration.WaitingAsync(sample, cancellationToken));
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

        private async Task<PipelineFrameStatus> FrameAsync(CancellationToken cancellationToken) =>
            new(await _frame.StatusAsync(cancellationToken), new PipelineRoster(
                _roster.ReaderA, LaneModelRoster.Family(_roster.ReaderA),
                _roster.ReaderB, LaneModelRoster.Family(_roster.ReaderB),
                _roster.Auditor, LaneModelRoster.Family(_roster.Auditor)));

        private async Task<PipelineGateStatus> GateAsync()
        {
            var states = _lanes.GetLanes().ToDictionary(l => l.Lane, StringComparer.OrdinalIgnoreCase);
            var lanes = new List<PipelineGateLane>();
            foreach (var (lane, label) in AiLanes.All)
            {
                lanes.Add(new PipelineGateLane(lane, label, await _features.IsEnabledAsync(AiLanes.PauseKey(lane)),
                    states.GetValueOrDefault(lane) ?? new AiLaneStatusModel { Lane = lane }, AiLanes.HeldByPauseAll(lane)));
            }
            return new PipelineGateStatus(await _features.IsEnabledAsync(AiLanes.PauseAllKey), lanes, _lanes.GetHosts(),
                await _features.IsEnabledAsync(IPlatformFeatureService.Keys.AutoApprovePause));
        }
    }
}
