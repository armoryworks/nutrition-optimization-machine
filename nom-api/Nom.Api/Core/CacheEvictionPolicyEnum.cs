namespace Nom.Api.Core
{

    /// <summary>
    /// Cache eviction policy
    /// </summary>
    public enum CacheEvictionPolicyEnum
    {
        /// <summary>
        /// Least recently used
        /// </summary>
        LeastRecentlyUsed,

        /// <summary>
        /// Least frequently used
        /// </summary>
        LeastFrequentlyUsed,

        /// <summary>
        /// First in, first out
        /// </summary>
        FirstInFirstOut,

        /// <summary>
        /// Random
        /// </summary>
        Random,

        /// <summary>
        /// Time-based
        /// </summary>
        TimeBased
    }
}