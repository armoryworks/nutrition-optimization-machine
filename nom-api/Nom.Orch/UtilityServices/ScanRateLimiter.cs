using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.UtilityServices
{
    public sealed class ScanRateLimiter : IScanRateLimiter
    {
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

        public bool TryAcquire(string key, int limit, TimeSpan window)
        {
            var now = DateTime.UtcNow;
            var hits = _hits.GetOrAdd(key, _ => new Queue<DateTime>());
            lock (hits)
            {
                while (hits.Count > 0 && now - hits.Peek() > window) hits.Dequeue();
                if (hits.Count >= limit) return false;
                hits.Enqueue(now);
                return true;
            }
        }
    }
}
