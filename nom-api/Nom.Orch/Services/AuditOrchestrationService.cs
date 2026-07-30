// File: Nom.Orch/Services/AuditOrchestrationService.cs

using Microsoft.AspNetCore.Http;
using Nom.Data;
using Nom.Data.Privacy;
using Nom.Orch.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the logic for creating audit log entries.
    /// </summary>
    public class AuditOrchestrationService : IAuditOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly ICurrentUserService _currentUser;

        public AuditOrchestrationService(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor, ICurrentUserService currentUser)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
        }

        public async Task LogAsync(long personId, string actionType, string details)
        {
            var actorId = GetCurrentPersonId();
            // CORRECTED: Use null-coalescing operator to provide a default empty string
            var ipAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
            var userAgent = _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? string.Empty;

            var logEntry = new DataProcessingLogEntity
            {
                PersonId = personId, // The person whose data is being affected
                ActionType = actionType,
                ActorId = actorId, // The person performing the action
                Timestamp = DateTime.UtcNow,
                Details = details,
                IpAddress = ipAddress,
                UserAgent = userAgent
            };

            _dbContext.DataProcessingLogs.Add(logEntry);
            await _dbContext.SaveChangesAsync();
        }

        // System fallback: audit rows written outside a user context (e.g. registration)
        // are attributed to the System person.
        private long GetCurrentPersonId() => _currentUser.PersonIdOrSystem;
    }
}
