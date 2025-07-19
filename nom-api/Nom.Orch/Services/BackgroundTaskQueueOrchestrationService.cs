// File: Nom.Orch/Services/BackgroundTaskQueueOrchestrationService.cs

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// An in-memory implementation of a background task queue.
    /// </summary>
    public class BackgroundTaskQueueOrchestrationService : IBackgroundTaskQueueOrchestrationService
    {
        private readonly ConcurrentQueue<Func<CancellationToken, Task>> _workItems = new();
        private readonly SemaphoreSlim _signal = new(0);

        /// <inheritdoc />
        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        /// <inheritdoc />
        public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);

            return workItem!;
        }
    }
}
