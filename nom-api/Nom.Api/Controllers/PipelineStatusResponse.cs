using Nom.Api.Services;
using Nom.Orch.Models.Curation;

namespace Nom.Api.Controllers
{
    /// <summary>The pipeline counts, cached, and the model lanes as the lane gate sees them now.</summary>
    public sealed record PipelineStatusResponse(PipelineStatusModel Status, PipelineGateStatus Gate);

    /// <summary>The pause switches and the live state of every model lane and model host.</summary>
    public sealed record PipelineGateStatus(bool PauseAll, IReadOnlyList<PipelineGateLane> Lanes, IReadOnlyList<AiHostStatusModel> Hosts);

    /// <summary>One model lane: its pause switch (platform feature ai-pause-{lane}) and its state.</summary>
    public sealed record PipelineGateLane(string Lane, string Label, bool Paused, AiLaneStatusModel Status);

    public sealed class SetPipelinePauseRequest
    {
        public bool IsEnabled { get; set; }
    }
}
