using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Session management service for tracking and controlling user sessions
    /// Provides session tracking, concurrent session limits, and session invalidation
    /// </summary>
    public class SessionManagementService : ISessionManagementService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<SessionManagementService> _logger;

        // Session configuration
        private const int MAX_CONCURRENT_SESSIONS = 3;
        private const int SESSION_TIMEOUT_MINUTES = 1440; // 24 hours
        private const string SESSION_CACHE_PREFIX = "session:";
        private const string USER_SESSIONS_PREFIX = "user_sessions:";

        public SessionManagementService(IMemoryCache cache, ILogger<SessionManagementService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Creates a new session for a user
        /// </summary>
        public async Task<SessionInfo> CreateSessionAsync(string userId, string deviceInfo, string ipAddress)
        {
            try
            {
                var sessionId = Guid.NewGuid().ToString();
                var sessionInfo = new SessionInfo
                {
                    SessionId = sessionId,
                    UserId = userId,
                    DeviceInfo = deviceInfo,
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow,
                    IsActive = true
                };

                // Store session info
                var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                _cache.Set(sessionKey, sessionInfo, TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES));

                // Add to user's active sessions
                await AddUserSessionAsync(userId, sessionId);

                _logger.LogInformation("Created session {SessionId} for user {UserId}", sessionId, userId);
                return sessionInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create session for user {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Validates a session and updates last activity
        /// </summary>
        public async Task<bool> ValidateSessionAsync(string sessionId)
        {
            try
            {
                var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                if (!_cache.TryGetValue(sessionKey, out SessionInfo sessionInfo))
                {
                    _logger.LogWarning("Session {SessionId} not found", sessionId);
                    return false;
                }

                if (!sessionInfo.IsActive)
                {
                    _logger.LogWarning("Session {SessionId} is inactive", sessionId);
                    return false;
                }

                // Check if session has expired
                if (DateTime.UtcNow.Subtract(sessionInfo.LastActivity).TotalMinutes > SESSION_TIMEOUT_MINUTES)
                {
                    _logger.LogWarning("Session {SessionId} has expired", sessionId);
                    await InvalidateSessionAsync(sessionId);
                    return false;
                }

                // Update last activity
                sessionInfo.LastActivity = DateTime.UtcNow;
                _cache.Set(sessionKey, sessionInfo, TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating session {SessionId}", sessionId);
                return false;
            }
        }

        /// <summary>
        /// Gets session information
        /// </summary>
        public SessionInfo? GetSessionInfo(string sessionId)
        {
            try
            {
                var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                _cache.TryGetValue(sessionKey, out SessionInfo sessionInfo);
                return sessionInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session info for {SessionId}", sessionId);
                return null;
            }
        }

        /// <summary>
        /// Gets all active sessions for a user
        /// </summary>
        public async Task<List<SessionInfo>> GetUserSessionsAsync(string userId)
        {
            try
            {
                var userSessionsKey = $"{USER_SESSIONS_PREFIX}{userId}";
                if (!_cache.TryGetValue(userSessionsKey, out List<string> sessionIds))
                {
                    return new List<SessionInfo>();
                }

                var sessions = new List<SessionInfo>();
                foreach (var sessionId in sessionIds)
                {
                    var sessionInfo = GetSessionInfo(sessionId);
                    if (sessionInfo != null && sessionInfo.IsActive)
                    {
                        sessions.Add(sessionInfo);
                    }
                }

                return sessions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sessions for user {UserId}", userId);
                return new List<SessionInfo>();
            }
        }

        /// <summary>
        /// Invalidates a specific session
        /// </summary>
        public async Task<bool> InvalidateSessionAsync(string sessionId)
        {
            try
            {
                var sessionInfo = GetSessionInfo(sessionId);
                if (sessionInfo == null)
                {
                    return false;
                }

                // Mark session as inactive
                sessionInfo.IsActive = false;
                var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                _cache.Set(sessionKey, sessionInfo, TimeSpan.FromMinutes(5)); // Keep for 5 minutes for audit

                // Remove from user's active sessions
                await RemoveUserSessionAsync(sessionInfo.UserId, sessionId);

                _logger.LogInformation("Invalidated session {SessionId} for user {UserId}", sessionId, sessionInfo.UserId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating session {SessionId}", sessionId);
                return false;
            }
        }

        /// <summary>
        /// Invalidates all sessions for a user
        /// </summary>
        public async Task<bool> InvalidateAllUserSessionsAsync(string userId)
        {
            try
            {
                var sessions = await GetUserSessionsAsync(userId);
                foreach (var session in sessions)
                {
                    await InvalidateSessionAsync(session.SessionId);
                }

                _logger.LogInformation("Invalidated all sessions for user {UserId}", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating all sessions for user {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Checks if user has reached maximum concurrent sessions
        /// </summary>
        public async Task<bool> CanCreateNewSessionAsync(string userId)
        {
            try
            {
                var activeSessions = await GetUserSessionsAsync(userId);
                return activeSessions.Count < MAX_CONCURRENT_SESSIONS;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking session limit for user {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Gets session statistics for a user
        /// </summary>
        public async Task<SessionStatistics> GetSessionStatisticsAsync(string userId)
        {
            try
            {
                var sessions = await GetUserSessionsAsync(userId);
                var now = DateTime.UtcNow;

                return new SessionStatistics
                {
                    TotalActiveSessions = sessions.Count,
                    MaxConcurrentSessions = MAX_CONCURRENT_SESSIONS,
                    OldestSession = sessions.Any() ? sessions.Min(s => s.CreatedAt) : null,
                    NewestSession = sessions.Any() ? sessions.Max(s => s.CreatedAt) : null,
                    SessionsByDevice = sessions.GroupBy(s => s.DeviceInfo).ToDictionary(g => g.Key, g => g.Count()),
                    SessionsByIp = sessions.GroupBy(s => s.IpAddress).ToDictionary(g => g.Key, g => g.Count())
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session statistics for user {UserId}", userId);
                return new SessionStatistics();
            }
        }

        /// <summary>
        /// Cleans up expired sessions
        /// </summary>
        public async Task CleanupExpiredSessionsAsync()
        {
            try
            {
                // This would typically be called by a background service
                // For now, we'll rely on cache expiration
                _logger.LogInformation("Session cleanup completed");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }
        }

        /// <summary>
        /// Adds a session to user's active sessions list
        /// </summary>
        private async Task AddUserSessionAsync(string userId, string sessionId)
        {
            try
            {
                var userSessionsKey = $"{USER_SESSIONS_PREFIX}{userId}";
                var sessionIds = new List<string>();

                if (_cache.TryGetValue(userSessionsKey, out List<string> existingSessions))
                {
                    sessionIds = existingSessions;
                }

                if (!sessionIds.Contains(sessionId))
                {
                    sessionIds.Add(sessionId);
                    _cache.Set(userSessionsKey, sessionIds, TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding session {SessionId} to user {UserId}", sessionId, userId);
            }
        }

        /// <summary>
        /// Removes a session from user's active sessions list
        /// </summary>
        private async Task RemoveUserSessionAsync(string userId, string sessionId)
        {
            try
            {
                var userSessionsKey = $"{USER_SESSIONS_PREFIX}{userId}";
                if (_cache.TryGetValue(userSessionsKey, out List<string> sessionIds))
                {
                    sessionIds.Remove(sessionId);
                    if (sessionIds.Count > 0)
                    {
                        _cache.Set(userSessionsKey, sessionIds, TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES));
                    }
                    else
                    {
                        _cache.Remove(userSessionsKey);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing session {SessionId} from user {UserId}", sessionId, userId);
            }
        }

        public class SessionInfo
        {
            public string SessionId { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string DeviceInfo { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime LastActivity { get; set; }
            public bool IsActive { get; set; }
        }

        public class SessionStatistics
        {
            public int TotalActiveSessions { get; set; }
            public int MaxConcurrentSessions { get; set; }
            public DateTime? OldestSession { get; set; }
            public DateTime? NewestSession { get; set; }
            public Dictionary<string, int> SessionsByDevice { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> SessionsByIp { get; set; } = new Dictionary<string, int>();
        }
    }
} 