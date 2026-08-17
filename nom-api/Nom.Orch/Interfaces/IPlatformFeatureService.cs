using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Platform;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Platform-wide feature switches, controlled by an operator from the admin
    /// UI. Reads are cached briefly because they sit on request paths; a write
    /// invalidates the cache so a toggle takes effect immediately.
    /// </summary>
    public interface IPlatformFeatureService
    {
        /// <summary>Well-known keys, so callers never hand-write the strings.</summary>
        public static class Keys
        {
            /// <summary>The Brigade provider platform (console, enrollment, provider policy).</summary>
            public const string Brigade = "brigade";
        }

        Task<bool> IsEnabledAsync(string key);

        Task<List<PlatformFeatureModel>> ListAsync();

        /// <summary>Creates the feature if it is not yet known. Returns the new state.</summary>
        Task<PlatformFeatureModel> SetAsync(string key, bool isEnabled, long adminPersonId);
    }
}
