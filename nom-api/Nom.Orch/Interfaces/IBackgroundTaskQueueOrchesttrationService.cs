// File: Nom.Orch/Interfaces/IBackgroundTaskQueueOrchestrationService.cs

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines a queue for background tasks that are processed out of band.
    /// </summary>
    public interface IBackgroundTaskQueueOrchestrationService
    {
        /// <summary>
        /// Adds a work item to the queue.
        /// </summary>
        /// <param name="workItem">The work item to be executed.</param>
        void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);

        /// <summary>
        /// Dequeues and returns a work item from the queue.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The dequeued work item.</returns>
        Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
    }
}
