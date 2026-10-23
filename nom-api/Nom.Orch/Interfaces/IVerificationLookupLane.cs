using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Hand-off point for the server-side lookup lane that will corroborate a household's label,
    /// barcode or measurements against published sources. This release ships only the no-op:
    /// nothing is fetched from anywhere, and a queued verification simply stays pending.
    /// </summary>
    public interface IVerificationLookupLane
    {
        bool IsConfigured { get; }
        Task QueueAsync(long verificationId, CancellationToken cancellationToken = default);
    }
}
