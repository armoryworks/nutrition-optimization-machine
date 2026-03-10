using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Advanced monitoring service for real-time security monitoring and threat detection
    /// Provides comprehensive monitoring, alerting, and threat intelligence
    /// </summary>
    public class AdvancedMonitoringService : IAdvancedMonitoringService
    {
        private readonly ILogger<AdvancedMonitoringService> _logger;
        private readonly Timer _monitoringTimer;
        private readonly List<SecurityEvent> _securityEvents;
        private readonly object _lockObject = new object();

        // Monitoring thresholds
        private const int MAX_FAILED_LOGINS_PER_HOUR = 10;
        private const int MAX_REQUESTS_PER_MINUTE = 100;
        private const int MAX_SUSPICIOUS_PATTERNS_PER_HOUR = 5;
        private const int MEMORY_THRESHOLD_MB = 500;
        private const int CPU_THRESHOLD_PERCENT = 80;

        public AdvancedMonitoringService(ILogger<AdvancedMonitoringService> logger)
        {
            _logger = logger;
            _securityEvents = new List<SecurityEvent>();

            // Start monitoring timer (check every 30 seconds)
            _monitoringTimer = new Timer(PerformMonitoringCheck, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Records a security event
        /// </summary>
        public void RecordSecurityEvent(SecurityEventType eventType, string description, string? userId = null, string? ipAddress = null)
        {
            try
            {
                var securityEvent = new SecurityEvent
                {
                    Id = Guid.NewGuid(),
                    EventType = eventType,
                    Description = description,
                    UserId = userId,
                    IpAddress = ipAddress,
                    Timestamp = DateTime.UtcNow,
                    Severity = GetEventSeverity(eventType)
                };

                lock (_lockObject)
                {
                    _securityEvents.Add(securityEvent);

                    // Keep only last 1000 events
                    if (_securityEvents.Count > 1000)
                    {
                        _securityEvents.RemoveRange(0, _securityEvents.Count - 1000);
                    }
                }

                _logger.LogInformation("Security event recorded: {EventType} - {Description}", eventType, description);

                // Check for immediate threats
                CheckForImmediateThreats(securityEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording security event");
            }
        }

        /// <summary>
        /// Gets security events for a time period
        /// </summary>
        public List<SecurityEvent> GetSecurityEvents(DateTime? since = null)
        {
            lock (_lockObject)
            {
                if (since.HasValue)
                {
                    return _securityEvents.Where(e => e.Timestamp >= since.Value).ToList();
                }
                return _securityEvents.ToList();
            }
        }

        /// <summary>
        /// Gets security statistics
        /// </summary>
        public SecurityStatistics GetSecurityStatistics()
        {
            var now = DateTime.UtcNow;
            var oneHourAgo = now.AddHours(-1);
            var oneDayAgo = now.AddDays(-1);

            lock (_lockObject)
            {
                var recentEvents = _securityEvents.Where(e => e.Timestamp >= oneHourAgo).ToList();
                var dailyEvents = _securityEvents.Where(e => e.Timestamp >= oneDayAgo).ToList();

                return new SecurityStatistics
                {
                    TotalEvents = _securityEvents.Count,
                    EventsLastHour = recentEvents.Count,
                    EventsLastDay = dailyEvents.Count,
                    CriticalEvents = _securityEvents.Count(e => e.Severity == SecurityEventSeverity.Critical),
                    HighSeverityEvents = _securityEvents.Count(e => e.Severity == SecurityEventSeverity.High),
                    MediumSeverityEvents = _securityEvents.Count(e => e.Severity == SecurityEventSeverity.Medium),
                    LowSeverityEvents = _securityEvents.Count(e => e.Severity == SecurityEventSeverity.Low),
                    FailedLoginAttempts = recentEvents.Count(e => e.EventType == SecurityEventType.FailedLogin),
                    SuspiciousActivity = recentEvents.Count(e => e.EventType == SecurityEventType.SuspiciousActivity),
                    UnauthorizedAccess = recentEvents.Count(e => e.EventType == SecurityEventType.UnauthorizedAccess),
                    DataBreachAttempts = recentEvents.Count(e => e.EventType == SecurityEventType.DataBreachAttempt),
                    LastEventTimestamp = _securityEvents.Any() ? _securityEvents.Max(e => e.Timestamp) : null
                };
            }
        }

        /// <summary>
        /// Performs real-time monitoring check
        /// </summary>
        private void PerformMonitoringCheck(object? state)
        {
            try
            {
                // Check for failed login patterns
                CheckFailedLoginPatterns();

                // Check for suspicious activity patterns
                CheckSuspiciousActivityPatterns();

                // Check for resource usage anomalies
                CheckResourceUsageAnomalies();

                // Check for geographic anomalies
                CheckGeographicAnomalies();

                // Check for time-based anomalies
                CheckTimeBasedAnomalies();

                // Generate alerts for critical issues
                GenerateAlerts();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during monitoring check");
            }
        }

        /// <summary>
        /// Checks for failed login patterns
        /// </summary>
        private void CheckFailedLoginPatterns()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentFailedLogins = GetSecurityEvents(oneHourAgo)
                .Where(e => e.EventType == SecurityEventType.FailedLogin)
                .ToList();

            // Check for excessive failed logins from same IP
            var failedLoginsByIp = recentFailedLogins
                .GroupBy(e => e.IpAddress)
                .Where(g => g.Count() > MAX_FAILED_LOGINS_PER_HOUR)
                .ToList();

            foreach (var group in failedLoginsByIp)
            {
                RecordSecurityEvent(
                    SecurityEventType.BruteForceAttempt,
                    $"Excessive failed logins from IP {group.Key}: {group.Count()} attempts",
                    ipAddress: group.Key
                );
            }

            // Check for failed logins from multiple IPs for same user
            var failedLoginsByUser = recentFailedLogins
                .Where(e => !string.IsNullOrEmpty(e.UserId))
                .GroupBy(e => e.UserId)
                .Where(g => g.Count() > MAX_FAILED_LOGINS_PER_HOUR)
                .ToList();

            foreach (var group in failedLoginsByUser)
            {
                RecordSecurityEvent(
                    SecurityEventType.AccountCompromise,
                    $"Multiple failed login attempts for user {group.Key}: {group.Count()} attempts",
                    userId: group.Key
                );
            }
        }

        /// <summary>
        /// Checks for suspicious activity patterns
        /// </summary>
        private void CheckSuspiciousActivityPatterns()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentSuspiciousActivity = GetSecurityEvents(oneHourAgo)
                .Where(e => e.EventType == SecurityEventType.SuspiciousActivity)
                .ToList();

            // Check for rapid-fire requests
            var rapidRequests = recentSuspiciousActivity
                .GroupBy(e => e.IpAddress)
                .Where(g => g.Count() > MAX_REQUESTS_PER_MINUTE)
                .ToList();

            foreach (var group in rapidRequests)
            {
                RecordSecurityEvent(
                    SecurityEventType.DenialOfService,
                    $"Rapid-fire requests detected from IP {group.Key}: {group.Count()} requests",
                    ipAddress: group.Key
                );
            }
        }

        /// <summary>
        /// Checks for resource usage anomalies
        /// </summary>
        private void CheckResourceUsageAnomalies()
        {
            var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024); // MB
            var cpuUsage = Environment.ProcessorCount;

            if (memoryUsage > MEMORY_THRESHOLD_MB)
            {
                RecordSecurityEvent(
                    SecurityEventType.ResourceAnomaly,
                    $"High memory usage detected: {memoryUsage} MB"
                );
            }

            // In a real implementation, you would get actual CPU usage
            if (cpuUsage > CPU_THRESHOLD_PERCENT)
            {
                RecordSecurityEvent(
                    SecurityEventType.ResourceAnomaly,
                    $"High CPU usage detected: {cpuUsage}%"
                );
            }
        }

        /// <summary>
        /// Checks for geographic anomalies
        /// </summary>
        private void CheckGeographicAnomalies()
        {
            // In a real implementation, this would use IP geolocation
            // For now, we'll simulate geographic anomaly detection
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentEvents = GetSecurityEvents(oneHourAgo)
                .Where(e => !string.IsNullOrEmpty(e.IpAddress))
                .ToList();

            // Check for logins from unusual locations
            var eventsByUser = recentEvents
                .Where(e => !string.IsNullOrEmpty(e.UserId))
                .GroupBy(e => e.UserId)
                .ToList();

            foreach (var userGroup in eventsByUser)
            {
                var uniqueIps = userGroup.Select(e => e.IpAddress).Distinct().Count();
                if (uniqueIps > 3) // More than 3 different IPs in an hour
                {
                    RecordSecurityEvent(
                        SecurityEventType.GeographicAnomaly,
                        $"User {userGroup.Key} accessed from {uniqueIps} different IP addresses",
                        userId: userGroup.Key
                    );
                }
            }
        }

        /// <summary>
        /// Checks for time-based anomalies
        /// </summary>
        private void CheckTimeBasedAnomalies()
        {
            var now = DateTime.UtcNow;
            var hour = now.Hour;

            // Check for unusual activity during off-hours (2 AM - 6 AM)
            if (hour >= 2 && hour <= 6)
            {
                var offHourEvents = GetSecurityEvents(now.AddHours(-1))
                    .Where(e => e.EventType == SecurityEventType.Login || e.EventType == SecurityEventType.DataAccess)
                    .ToList();

                if (offHourEvents.Count > 5)
                {
                    RecordSecurityEvent(
                        SecurityEventType.TimeBasedAnomaly,
                        $"Unusual activity during off-hours: {offHourEvents.Count} events"
                    );
                }
            }
        }

        /// <summary>
        /// Checks for immediate threats
        /// </summary>
        private void CheckForImmediateThreats(SecurityEvent securityEvent)
        {
            // Immediate response for critical events
            if (securityEvent.Severity == SecurityEventSeverity.Critical)
            {
                _logger.LogCritical("CRITICAL SECURITY EVENT: {EventType} - {Description}", 
                    securityEvent.EventType, securityEvent.Description);

                // In a real implementation, this would trigger immediate alerts
                // such as SMS, email, or integration with security systems
            }

            // Check for rapid escalation
            var recentCriticalEvents = GetSecurityEvents(DateTime.UtcNow.AddMinutes(-5))
                .Where(e => e.Severity == SecurityEventSeverity.Critical)
                .ToList();

            if (recentCriticalEvents.Count > 3)
            {
                _logger.LogCritical("SECURITY EMERGENCY: Multiple critical events detected in last 5 minutes");
                // Trigger emergency response
            }
        }

        /// <summary>
        /// Generates alerts for critical issues
        /// </summary>
        private void GenerateAlerts()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentEvents = GetSecurityEvents(oneHourAgo);

            // Generate alerts for high-severity events
            var highSeverityEvents = recentEvents
                .Where(e => e.Severity == SecurityEventSeverity.High || e.Severity == SecurityEventSeverity.Critical)
                .ToList();

            foreach (var securityEvent in highSeverityEvents)
            {
                _logger.LogWarning("SECURITY ALERT: {EventType} - {Description} from {IpAddress}", 
                    securityEvent.EventType, securityEvent.Description, securityEvent.IpAddress);
            }
        }

        /// <summary>
        /// Gets the severity for an event type
        /// </summary>
        private SecurityEventSeverity GetEventSeverity(SecurityEventType eventType)
        {
            return eventType switch
            {
                SecurityEventType.DataBreachAttempt => SecurityEventSeverity.Critical,
                SecurityEventType.BruteForceAttempt => SecurityEventSeverity.High,
                SecurityEventType.AccountCompromise => SecurityEventSeverity.High,
                SecurityEventType.DenialOfService => SecurityEventSeverity.High,
                SecurityEventType.UnauthorizedAccess => SecurityEventSeverity.High,
                SecurityEventType.SuspiciousActivity => SecurityEventSeverity.Medium,
                SecurityEventType.GeographicAnomaly => SecurityEventSeverity.Medium,
                SecurityEventType.TimeBasedAnomaly => SecurityEventSeverity.Medium,
                SecurityEventType.ResourceAnomaly => SecurityEventSeverity.Medium,
                SecurityEventType.FailedLogin => SecurityEventSeverity.Low,
                SecurityEventType.Login => SecurityEventSeverity.Low,
                SecurityEventType.DataAccess => SecurityEventSeverity.Low,
                _ => SecurityEventSeverity.Low
            };
        }


    }
} 