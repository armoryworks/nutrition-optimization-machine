using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Read-only counts of where recipes, recipe lines and foods are waiting in the automated
    /// catalog pipeline, with each lane's settings, queue and last change seen. Computed in one
    /// pass at a time and cached for a few minutes, because the line counts read every recipe line.
    /// </summary>
    public interface IPipelineStatusService
    {
        /// <summary>The cached status, or a new one when there is none or <paramref name="refresh"/> is set.</summary>
        Task<PipelineStatusModel> GetAsync(bool refresh = false, CancellationToken cancellationToken = default);
    }
}
