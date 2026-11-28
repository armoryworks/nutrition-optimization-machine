using Nom.Api.Services;
using Nom.Orch.Models.Curation;

namespace Nom.Api.Controllers
{
    /// <summary>The pipeline counts, cached, the model lanes as the lane gate sees them now, the lanes on the lane frame, and the reprocess lane's candidates.</summary>
    public sealed record PipelineStatusResponse(PipelineStatusModel Status, PipelineGateStatus Gate, PipelineFrameStatus? Frame = null, Nom.Orch.Models.Recipe.ReprocessLaneStatusModel? Reprocess = null,
        LinkRecheckStatusModel? LinkRecheck = null, Nom.Orch.Models.Curation.ContainerSizeStatusModel? ContainerSizes = null,
        Nom.Orch.Models.Recipe.SourceAdmissionStatusModel? Sources = null, Nom.Orch.Models.Recipe.SourceRightsStatusModel? SourceRights = null);

    /// <summary>Every lane and tier on the lane frame (curation.LaneState) with its figures, and the model roster.</summary>
    public sealed record PipelineFrameStatus(IReadOnlyList<LaneFrameStatusModel> Lanes, PipelineRoster Roster);

    /// <summary>The lane frame's models (settings Ai:Roster) and the family each belongs to.</summary>
    public sealed record PipelineRoster(string ReaderA, string ReaderAFamily, string ReaderB, string ReaderBFamily, string Auditor, string AuditorFamily);

    /// <summary>The pause switches and the live state of every model lane and model host, and whether recipe auto-approval is paused (platform feature pause-auto-approve).</summary>
    public sealed record PipelineGateStatus(bool PauseAll, IReadOnlyList<PipelineGateLane> Lanes, IReadOnlyList<AiHostStatusModel> Hosts, bool AutoApprovePaused = false);

    /// <summary>One model lane: its pause switch (platform feature ai-pause-{lane}), its state, and whether ai-pause-all holds it (not for a lane that only reads).</summary>
    public sealed record PipelineGateLane(string Lane, string Label, bool Paused, AiLaneStatusModel Status, bool HeldByPauseAll = true);

    public sealed class SetPipelinePauseRequest
    {
        public bool IsEnabled { get; set; }
    }
}
