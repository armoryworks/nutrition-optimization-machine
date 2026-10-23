using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    public class NoOpVerificationLookupLane : IVerificationLookupLane
    {
        public bool IsConfigured => false;

        public Task QueueAsync(long verificationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
