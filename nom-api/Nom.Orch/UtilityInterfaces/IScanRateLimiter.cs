using System;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Per-key sliding-window cap for uploads that cost CPU but leave no row to count
    /// (a barcode photo nothing could be decoded from). Process-local by design.
    /// </summary>
    public interface IScanRateLimiter
    {
        bool TryAcquire(string key, int limit, TimeSpan window);
    }
}
