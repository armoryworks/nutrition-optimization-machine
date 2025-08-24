using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;

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
        /// Validates a session
        /// </summary>
        public async Task<bool> ValidateSessionAsync(string sessionId)
        {
            try
            {
                var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                if (!_cache.TryGetValue(sessionKey, out SessionInfo? sessionInfo) || sessionInfo == null)
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
                _cache.TryGetValue(sessionKey, out SessionInfo? sessionInfo);
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
                _logger.LogInformation("Getting user sessions for user {UserId}", userId);

                var userSessionsKey = $"{USER_SESSIONS_PREFIX}{userId}";
                if (!_cache.TryGetValue(userSessionsKey, out List<string>? sessionIds) || sessionIds == null)
                {
                    return new List<SessionInfo>();
                }

                var sessions = new List<SessionInfo>();
                foreach (var sessionId in sessionIds)
                {
                    var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                    if (_cache.TryGetValue(sessionKey, out SessionInfo? sessionInfo) && sessionInfo != null)
                    {
                        sessions.Add(sessionInfo);
                    }
                }

                return sessions.OrderByDescending(s => s.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user sessions for user {UserId}", userId);
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
                _logger.LogInformation("Invalidating session {SessionId}", sessionId);

                var sessionKey = $"{SESSION_CACHE_PREFIX}{sessionId}";
                if (_cache.TryGetValue(sessionKey, out SessionInfo? sessionInfo) && sessionInfo != null)
                {
                    // Remove from user's session list
                    var userSessionsKey = $"{USER_SESSIONS_PREFIX}{sessionInfo.UserId}";
                    if (_cache.TryGetValue(userSessionsKey, out List<string>? userSessions) && userSessions != null)
                    {
                        userSessions.Remove(sessionId);
                        _cache.Set(userSessionsKey, userSessions, TimeSpan.FromHours(24));
                    }

                    // Remove session from cache
                    _cache.Remove(sessionKey);
                    return true;
                }

                return false;
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
                // Simulate async statistics gathering
                await Task.Delay(15);

                var activeSessions = await GetUserSessionsAsync(userId);
                var statistics = new SessionStatistics
                {
                    TotalActiveSessions = activeSessions.Count,
                    MaxConcurrentSessions = MAX_CONCURRENT_SESSIONS,
                    OldestSession = activeSessions.Min(s => s.CreatedAt),
                    NewestSession = activeSessions.Max(s => s.CreatedAt)
                };

                // Group sessions by device and IP
                foreach (var session in activeSessions)
                {
                    if (!string.IsNullOrEmpty(session.DeviceInfo))
                    {
                        statistics.SessionsByDevice[session.DeviceInfo] =
                            statistics.SessionsByDevice.GetValueOrDefault(session.DeviceInfo, 0) + 1;
                    }

                    if (!string.IsNullOrEmpty(session.IpAddress))
                    {
                        statistics.SessionsByIp[session.IpAddress] =
                            statistics.SessionsByIp.GetValueOrDefault(session.IpAddress, 0) + 1;
                    }
                }

                return statistics;
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
                _logger.LogInformation("Cleaning up expired sessions");

                var removedCount = 0;
                var cacheEntries = ((MemoryCache)_cache).Keys;
                var expiredSessions = new List<string>();

                foreach (var entry in cacheEntries)
                {
                    var key = entry.GetType()?.GetProperty("Key")?.GetValue(entry)?.ToString();
                    if (key != null && key.StartsWith(SESSION_CACHE_PREFIX))
                    {
                        if (_cache.TryGetValue(key, out SessionInfo? sessionInfo) && sessionInfo != null)
                        {
                            if (sessionInfo.LastActivity <= DateTime.UtcNow)
                            {
                                expiredSessions.Add(key);
                            }
                        }
                    }
                }

                foreach (var sessionKey in expiredSessions)
                {
                    if (_cache.TryGetValue(sessionKey, out SessionInfo? sessionInfo) && sessionInfo != null)
                    {
                        // Remove from user's session list
                        var userSessionsKey = $"{USER_SESSIONS_PREFIX}{sessionInfo.UserId}";
                        if (_cache.TryGetValue(userSessionsKey, out List<string>? userSessions) && userSessions != null)
                        {
                            var sessionId = sessionKey.Replace(SESSION_CACHE_PREFIX, "");
                            userSessions.Remove(sessionId);
                            _cache.Set(userSessionsKey, userSessions, TimeSpan.FromHours(24));
                        }

                        _cache.Remove(sessionKey);
                        removedCount++;
                    }
                }

                _logger.LogInformation("Removed {RemovedCount} expired sessions", removedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired sessions");
            }
        }

        /// <summary>
        /// Adds a session to user's active sessions list
        /// </summary>
        private async Task AddUserSessionAsync(string userId, string sessionId)
        {
            try
            {
                _logger.LogInformation("Adding session for user {UserId}", userId);

                var sessionKey = $"{USER_SESSIONS_PREFIX}{userId}";
                if (!_cache.TryGetValue(sessionKey, out List<string>? sessionIds) || sessionIds == null)
                {
                    sessionIds = new List<string>();
                }

                if (!sessionIds.Contains(sessionId))
                {
                    sessionIds.Add(sessionId);
                    _cache.Set(sessionKey, sessionIds, TimeSpan.FromDays(1));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding session {SessionId} for user {UserId}", sessionId, userId);
            }
        }

        /// <summary>
        /// Removes a session from user's active sessions list
        /// </summary>
        private async Task RemoveUserSessionAsync(string userId, string sessionId)
        {
            try
            {
                _logger.LogInformation("Removing session {SessionId} for user {UserId}", sessionId, userId);

                var userSessionsKey = $"{USER_SESSIONS_PREFIX}{userId}";
                if (_cache.TryGetValue(userSessionsKey, out List<string>? sessionIds) && sessionIds != null)
                {
                    sessionIds.Remove(sessionId);
                    if (sessionIds.Count == 0)
                    {
                        _cache.Remove(userSessionsKey);
                    }
                    else
                    {
                        _cache.Set(userSessionsKey, sessionIds, TimeSpan.FromDays(1));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing session {SessionId} for user {UserId}", sessionId, userId);
            }
        }


    }
}