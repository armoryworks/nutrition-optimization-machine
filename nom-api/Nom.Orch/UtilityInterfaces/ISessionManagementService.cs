using Nom.Orch.UtilityServices;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Session Management service
    /// </summary>
    public interface ISessionManagementService
    {
        /// <summary>
        /// Creates a new session for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="deviceInfo">Device information</param>
        /// <param name="ipAddress">IP address</param>
        /// <returns>Session information</returns>
        Task<SessionManagementService.SessionInfo> CreateSessionAsync(string userId, string deviceInfo, string ipAddress);

        /// <summary>
        /// Validates a session and updates last activity
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>True if valid, false otherwise</returns>
        Task<bool> ValidateSessionAsync(string sessionId);

        /// <summary>
        /// Gets session information
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>Session information or null</returns>
        SessionManagementService.SessionInfo? GetSessionInfo(string sessionId);

        /// <summary>
        /// Gets all active sessions for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>List of active sessions</returns>
        Task<List<SessionManagementService.SessionInfo>> GetUserSessionsAsync(string userId);

        /// <summary>
        /// Invalidates a specific session
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <returns>True if successful</returns>
        Task<bool> InvalidateSessionAsync(string sessionId);

        /// <summary>
        /// Invalidates all sessions for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>True if successful</returns>
        Task<bool> InvalidateAllUserSessionsAsync(string userId);

        /// <summary>
        /// Checks if user has reached maximum concurrent sessions
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>True if can create new session</returns>
        Task<bool> CanCreateNewSessionAsync(string userId);

        /// <summary>
        /// Gets session statistics for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>Session statistics</returns>
        Task<SessionManagementService.SessionStatistics> GetSessionStatisticsAsync(string userId);

        /// <summary>
        /// Cleans up expired sessions
        /// </summary>
        Task CleanupExpiredSessionsAsync();
    }
} 