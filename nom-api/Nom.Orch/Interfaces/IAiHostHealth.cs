using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Tells a lane whether the model host it calls is answering, so a call that timed out can be
    /// told apart from a host that is down: a slow item on a live host is the item's fault and may
    /// be recorded, an outage must cost no attempts.
    /// </summary>
    public interface IAiHostHealth
    {
        /// <summary>Probes the hosts the lane has called now; true when every one of them answers.</summary>
        Task<bool> AnswersAsync(string lane, CancellationToken cancellationToken = default);
    }
}
