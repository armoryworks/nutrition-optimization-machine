using Nom.Orch.UtilityServices;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Data Retention service
    /// </summary>
    public interface IDataRetentionService
    {
        /// <summary>
        /// Executes data retention cleanup for all data types
        /// </summary>
        /// <returns>Data retention report</returns>
        Task<DataRetentionReport> ExecuteDataRetentionCleanupAsync();

        /// <summary>
        /// Gets data retention statistics
        /// </summary>
        /// <returns>Data retention statistics</returns>
        Task<DataRetentionStatistics> GetDataRetentionStatisticsAsync();
    }
} 