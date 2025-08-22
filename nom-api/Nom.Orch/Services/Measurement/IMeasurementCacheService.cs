using Nom.Orch.Models.Measurement;
using Nom.Data.Measurement;

namespace Nom.Orch.Services.Measurement
{
    /// <summary>
    /// Interface for caching measurement data to improve performance
    /// </summary>
    public interface IMeasurementCacheService
    {
        /// <summary>
        /// Gets cached conversion factor between two measurements
        /// </summary>
        Task<decimal?> GetCachedConversionAsync(long fromId, long toId);

        /// <summary>
        /// Caches conversion factor between two measurements
        /// </summary>
        Task CacheConversionAsync(long fromId, long toId, decimal conversionFactor, decimal? offset = null);

        /// <summary>
        /// Gets cached measurement by ID
        /// </summary>
        Task<MeasurementModel?> GetCachedMeasurementAsync(long id);

        /// <summary>
        /// Caches measurement data
        /// </summary>
        Task CacheMeasurementAsync(MeasurementModel measurement);

        /// <summary>
        /// Gets cached conversion path between two measurements
        /// </summary>
        Task<List<MeasurementConversionModel>?> GetCachedConversionPathAsync(long fromId, long toId);

        /// <summary>
        /// Caches conversion path between two measurements
        /// </summary>
        Task CacheConversionPathAsync(long fromId, long toId, List<MeasurementConversionModel> path);

        /// <summary>
        /// Gets cached conversion path entities between two measurements
        /// </summary>
        Task<List<MeasurementConversionEntity>?> GetCachedConversionPathEntitiesAsync(long fromId, long toId);

        /// <summary>
        /// Caches conversion path entities between two measurements
        /// </summary>
        Task CacheConversionPathEntitiesAsync(long fromId, long toId, List<MeasurementConversionEntity> path);

        /// <summary>
        /// Invalidates all cached data for a specific measurement
        /// </summary>
        Task InvalidateMeasurementCacheAsync(long measurementId);

        /// <summary>
        /// Clears all cached data
        /// </summary>
        Task ClearCacheAsync();

        /// <summary>
        /// Tries to get cached measurements by category
        /// </summary>
        bool TryGetCachedMeasurementsByCategory(long categoryId, out List<MeasurementModel> measurements);

        /// <summary>
        /// Caches measurements by category
        /// </summary>
        Task CacheMeasurementsByCategoryAsync(long categoryId, List<MeasurementModel> measurements);
    }
}
