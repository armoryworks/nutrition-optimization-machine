using Nom.Orch.UtilityServices;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Advanced Monitoring service
    /// </summary>
    public interface IAdvancedMonitoringService
    {
        /// <summary>
        /// Records a security event
        /// </summary>
        /// <param name="eventType">The type of security event</param>
        /// <param name="description">Event description</param>
        /// <param name="userId">User ID (optional)</param>
        /// <param name="ipAddress">IP address (optional)</param>
        void RecordSecurityEvent(AdvancedMonitoringService.SecurityEventType eventType, string description, string? userId = null, string? ipAddress = null);

        /// <summary>
        /// Gets security events for a time period
        /// </summary>
        /// <param name="since">Start time (optional)</param>
        /// <returns>List of security events</returns>
        List<AdvancedMonitoringService.SecurityEvent> GetSecurityEvents(DateTime? since = null);

        /// <summary>
        /// Gets security statistics
        /// </summary>
        /// <returns>Security statistics</returns>
        AdvancedMonitoringService.SecurityStatistics GetSecurityStatistics();
    }
} 