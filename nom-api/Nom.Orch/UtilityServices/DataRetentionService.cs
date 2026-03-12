using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Data retention service for automatic cleanup of old data according to GDPR requirements
    /// Implements data retention policies and automatic data cleanup
    /// </summary>
    public class DataRetentionService : IDataRetentionService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<DataRetentionService> _logger;

        // Data retention periods (in days)
        private const int USER_ACTIVITY_RETENTION_DAYS = 730; // 2 years
        private const int AUDIT_LOG_RETENTION_DAYS = 1095; // 3 years
        private const int PRIVACY_REQUEST_RETENTION_DAYS = 2555; // 7 years
        private const int SESSION_DATA_RETENTION_DAYS = 90; // 3 months
        private const int TEMP_FILE_RETENTION_DAYS = 7; // 1 week
        private const int FAILED_LOGIN_RETENTION_DAYS = 30; // 1 month

        public DataRetentionService(ApplicationDbContext dbContext, ILogger<DataRetentionService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Executes data retention cleanup for all data types
        /// </summary>
        public async Task<DataRetentionReport> ExecuteDataRetentionCleanupAsync()
        {
            var report = new DataRetentionReport
            {
                ExecutionTime = DateTime.UtcNow,
                CleanupResults = new List<CleanupResult>()
            };

            try
            {
                _logger.LogInformation("Starting data retention cleanup");

                // Clean up old user activity data
                report.CleanupResults.Add(await CleanupUserActivityDataAsync());

                // Clean up old audit logs
                report.CleanupResults.Add(await CleanupAuditLogsAsync());

                // Clean up old privacy requests
                report.CleanupResults.Add(await CleanupPrivacyRequestsAsync());

                // Clean up old session data
                report.CleanupResults.Add(await CleanupSessionDataAsync());

                // Clean up temporary files
                report.CleanupResults.Add(await CleanupTemporaryFilesAsync());

                // Clean up failed login attempts
                report.CleanupResults.Add(await CleanupFailedLoginAttemptsAsync());

                // Clean up orphaned data
                report.CleanupResults.Add(await CleanupOrphanedDataAsync());

                report.Success = true;
                _logger.LogInformation("Data retention cleanup completed successfully");
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error during data retention cleanup");
            }

            return report;
        }

        /// <summary>
        /// Cleans up old user activity data
        /// </summary>
        private async Task<CleanupResult> CleanupUserActivityDataAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-USER_ACTIVITY_RETENTION_DAYS);
                var deletedCount = 0;

                // Clean up old user ratings
                var oldRatings = await _dbContext.RecipeRatings
                    .Where(r => r.CreatedDate < cutoffDate)
                    .ToListAsync();

                if (oldRatings.Any())
                {
                    _dbContext.RecipeRatings.RemoveRange(oldRatings);
                    deletedCount += oldRatings.Count;
                }

                // Clean up old user comments
                var oldComments = await _dbContext.RecipeComments
                    .Where(c => c.CreatedDate < cutoffDate)
                    .ToListAsync();

                if (oldComments.Any())
                {
                    _dbContext.RecipeComments.RemoveRange(oldComments);
                    deletedCount += oldComments.Count;
                }

                await _dbContext.SaveChangesAsync();

                return new CleanupResult
                {
                    DataType = "UserActivity",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = USER_ACTIVITY_RETENTION_DAYS,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up user activity data");
                return new CleanupResult
                {
                    DataType = "UserActivity",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up old audit logs
        /// </summary>
        private async Task<CleanupResult> CleanupAuditLogsAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-AUDIT_LOG_RETENTION_DAYS);
                var deletedCount = 0;

                // Note: In a real implementation, you would have an AuditLog table
                // For now, we'll simulate this with existing audit-related data

                // Clean up old privacy requests that are completed
                var oldPrivacyRequests = await _dbContext.PrivacyRequests
                    .Where(p => p.CompletionTimestamp.HasValue && 
                               p.CompletionTimestamp.Value < cutoffDate &&
                               p.Status == "Completed")
                    .ToListAsync();

                if (oldPrivacyRequests.Any())
                {
                    _dbContext.PrivacyRequests.RemoveRange(oldPrivacyRequests);
                    deletedCount += oldPrivacyRequests.Count;
                }

                await _dbContext.SaveChangesAsync();

                return new CleanupResult
                {
                    DataType = "AuditLogs",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = AUDIT_LOG_RETENTION_DAYS,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up audit logs");
                return new CleanupResult
                {
                    DataType = "AuditLogs",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up old privacy requests
        /// </summary>
        private async Task<CleanupResult> CleanupPrivacyRequestsAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-PRIVACY_REQUEST_RETENTION_DAYS);
                var deletedCount = 0;

                // Clean up very old privacy requests (keep for 7 years for legal compliance)
                var oldPrivacyRequests = await _dbContext.PrivacyRequests
                    .Where(p => p.RequestTimestamp < cutoffDate)
                    .ToListAsync();

                if (oldPrivacyRequests.Any())
                {
                    _dbContext.PrivacyRequests.RemoveRange(oldPrivacyRequests);
                    deletedCount += oldPrivacyRequests.Count;
                }

                await _dbContext.SaveChangesAsync();

                return new CleanupResult
                {
                    DataType = "PrivacyRequests",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = PRIVACY_REQUEST_RETENTION_DAYS,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up privacy requests");
                return new CleanupResult
                {
                    DataType = "PrivacyRequests",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up old session data
        /// </summary>
        private async Task<CleanupResult> CleanupSessionDataAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-SESSION_DATA_RETENTION_DAYS);
                var deletedCount = 0;

                // TODO: Implement session cleanup once session persistence table exists
                // Currently sessions use in-memory cache, so no DB cleanup needed

                return new CleanupResult
                {
                    DataType = "SessionData",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = SESSION_DATA_RETENTION_DAYS,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up session data");
                return new CleanupResult
                {
                    DataType = "SessionData",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up temporary files
        /// </summary>
        private async Task<CleanupResult> CleanupTemporaryFilesAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-TEMP_FILE_RETENTION_DAYS);
                var deletedCount = 0;

                // Clean up old temporary recipe assets
                var oldTempAssets = await _dbContext.RecipeAssets
                    .Where(a => a.CreatedDate < cutoffDate && 
                               a.Description != null && 
                               a.Description.Contains("temp"))
                    .ToListAsync();

                if (oldTempAssets.Any())
                {
                    _dbContext.RecipeAssets.RemoveRange(oldTempAssets);
                    deletedCount += oldTempAssets.Count;
                }

                await _dbContext.SaveChangesAsync();

                return new CleanupResult
                {
                    DataType = "TemporaryFiles",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = TEMP_FILE_RETENTION_DAYS,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up temporary files");
                return new CleanupResult
                {
                    DataType = "TemporaryFiles",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up failed login attempts
        /// </summary>
        private async Task<CleanupResult> CleanupFailedLoginAttemptsAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-FAILED_LOGIN_RETENTION_DAYS);
                var deletedCount = 0;

                // TODO: Implement failed login cleanup once login attempt tracking table exists

                return new CleanupResult
                {
                    DataType = "FailedLoginAttempts",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = FAILED_LOGIN_RETENTION_DAYS,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up failed login attempts");
                return new CleanupResult
                {
                    DataType = "FailedLoginAttempts",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up orphaned data
        /// </summary>
        private async Task<CleanupResult> CleanupOrphanedDataAsync()
        {
            try
            {
                var deletedCount = 0;

                // Clean up orphaned recipe assets (no associated recipe)
                var orphanedAssets = await _dbContext.RecipeAssets
                    .Where(a => !_dbContext.Recipes.Any(r => r.Id == a.RecipeId))
                    .ToListAsync();

                if (orphanedAssets.Any())
                {
                    _dbContext.RecipeAssets.RemoveRange(orphanedAssets);
                    deletedCount += orphanedAssets.Count;
                }

                // Clean up orphaned user consents (no associated person)
                var orphanedConsents = await _dbContext.UserConsents
                    .Where(uc => !_dbContext.Persons.Any(p => p.Id == uc.PersonId))
                    .ToListAsync();

                if (orphanedConsents.Any())
                {
                    _dbContext.UserConsents.RemoveRange(orphanedConsents);
                    deletedCount += orphanedConsents.Count;
                }

                await _dbContext.SaveChangesAsync();

                return new CleanupResult
                {
                    DataType = "OrphanedData",
                    RecordsDeleted = deletedCount,
                    RetentionPeriodDays = 0, // Immediate cleanup
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up orphaned data");
                return new CleanupResult
                {
                    DataType = "OrphanedData",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Gets data retention statistics
        /// </summary>
        public async Task<DataRetentionStatistics> GetDataRetentionStatisticsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;

                return new DataRetentionStatistics
                {
                    UserActivityRecords = await _dbContext.RecipeRatings.CountAsync(r => r.CreatedDate < now.AddDays(-USER_ACTIVITY_RETENTION_DAYS)),
                    AuditLogRecords = await _dbContext.PrivacyRequests.CountAsync(p => p.CompletionTimestamp < now.AddDays(-AUDIT_LOG_RETENTION_DAYS)),
                    PrivacyRequestRecords = await _dbContext.PrivacyRequests.CountAsync(p => p.RequestTimestamp < now.AddDays(-PRIVACY_REQUEST_RETENTION_DAYS)),
                    TemporaryFileRecords = await _dbContext.RecipeAssets.CountAsync(a => a.CreatedDate < now.AddDays(-TEMP_FILE_RETENTION_DAYS)),
                    OrphanedDataRecords = await _dbContext.RecipeAssets.CountAsync(a => !_dbContext.Recipes.Any(r => r.Id == a.RecipeId)),
                    LastCleanupDate = now // In real implementation, this would be stored
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting data retention statistics");
                return new DataRetentionStatistics();
            }
        }


    }
} 