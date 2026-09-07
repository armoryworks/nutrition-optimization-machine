// File: Nom.Orch/Services/PrivacyOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Privacy;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Privacy;
using Nom.Orch.UtilityInterfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class PrivacyOrchestrationService : IPrivacyOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IBackgroundTaskQueueOrchestrationService _taskQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PrivacyOrchestrationService> _logger;
        private readonly ISystemEmailService _email;

        // Account administration, not recipe curation: a data-subject request is a
        // user-account matter.
        private const string AdminClaimType = "CanManageUserRoles";

        public PrivacyOrchestrationService(
            ApplicationDbContext dbContext,
            IBackgroundTaskQueueOrchestrationService taskQueue,
            IServiceProvider serviceProvider,
            ILogger<PrivacyOrchestrationService> logger,
            ISystemEmailService email)
        {
            _dbContext = dbContext;
            _taskQueue = taskQueue;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _email = email;
        }

        public async Task<bool> UpdateConsentAsync(UpdateConsentRequest request, long personId)
        {
            var consentTypeRefIds = request.Consents.Select(c => c.ConsentTypeRefId).ToList();
            var consentTypes = await _dbContext.References
                .Where(r => consentTypeRefIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name);

            var existingUserConsents = await _dbContext.UserConsents
                .Where(uc => uc.PersonId == personId)
                .ToListAsync();

            foreach (var consentRequest in request.Consents)
            {
                if (!consentTypes.TryGetValue(consentRequest.ConsentTypeRefId, out var consentTypeName)) continue;

                var existingConsent = existingUserConsents.FirstOrDefault(uc => uc.ConsentType == consentTypeName);
                if (existingConsent == null)
                {
                    _dbContext.UserConsents.Add(new UserConsentEntity
                    {
                        PersonId = personId,
                        ConsentType = consentTypeName,
                        IsConsented = consentRequest.IsConsented,
                        ConsentTimestamp = DateTime.UtcNow,
                        ConsentVersion = "1.0",
                        LegalBasis = "Consent"
                    });
                }
                else
                {
                    existingConsent.IsConsented = consentRequest.IsConsented;
                    existingConsent.ConsentTimestamp = DateTime.UtcNow;
                    _dbContext.UserConsents.Update(existingConsent);
                }
            }
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<PrivacyRequestStatusResponse> RequestDataExportAsync(DataExportRequest request, long personId)
        {
            var privacyRequest = new PrivacyRequestEntity
            {
                PersonId = personId,
                RequestType = "DataExport",
                Status = "Pending",
                RequestTimestamp = DateTime.UtcNow,
                RequestDetails = $"{{ \"format\": \"{request.Format}\" }}"
            };
            _dbContext.PrivacyRequests.Add(privacyRequest);
            await _dbContext.SaveChangesAsync();
            await NotifyAdminsOfPrivacyRequestAsync(privacyRequest, personId);

            _taskQueue.QueueBackgroundWorkItem(async token =>
            {
                _logger.LogInformation("Starting data export for PersonId {PersonId}", personId);
                using var scope = _serviceProvider.CreateScope();
                var scopedDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var exportService = scope.ServiceProvider.GetRequiredService<IDataExportOrchestrationService>();

                var req = await scopedDbContext.PrivacyRequests.FindAsync(new object[] { privacyRequest.Id }, cancellationToken: token);
                if (req != null)
                {
                    req.Status = "Processing";
                    await scopedDbContext.SaveChangesAsync(token);

                    await exportService.ExportPersonDataAsync(personId, request.Format);

                    req.Status = "Completed";
                    req.CompletionTimestamp = DateTime.UtcNow;
                    await scopedDbContext.SaveChangesAsync(token);
                    _logger.LogInformation("Completed data export for PersonId {PersonId}", personId);
                }
            });

            return new PrivacyRequestStatusResponse
            {
                Success = true,
                Message = "Your data export request has been received and is being processed.",
                RequestId = privacyRequest.Id,
                Status = "Pending"
            };
        }

        public async Task<PrivacyRequestStatusResponse> RequestDataDeletionAsync(DataDeletionRequest request, long personId)
        {
            if (!request.Confirm)
            {
                return new PrivacyRequestStatusResponse { Success = false, Message = "Deletion request must be confirmed." };
            }

            var privacyRequest = new PrivacyRequestEntity
            {
                PersonId = personId,
                RequestType = "DataDeletion",
                Status = "Pending",
                RequestTimestamp = DateTime.UtcNow
            };
            _dbContext.PrivacyRequests.Add(privacyRequest);
            await _dbContext.SaveChangesAsync();
            await NotifyAdminsOfPrivacyRequestAsync(privacyRequest, personId);

            _taskQueue.QueueBackgroundWorkItem(async token =>
            {
                _logger.LogInformation("Starting data anonymization for PersonId {PersonId}", personId);
                using var scope = _serviceProvider.CreateScope();
                var scopedDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var anonymizationService = scope.ServiceProvider.GetRequiredService<IDataAnonymizationOrchestrationService>();

                var req = await scopedDbContext.PrivacyRequests.FindAsync(new object[] { privacyRequest.Id }, cancellationToken: token);
                if (req != null)
                {
                    req.Status = "Processing";
                    await scopedDbContext.SaveChangesAsync(token);

                    await anonymizationService.AnonymizePersonDataAsync(personId);

                    req.Status = "Completed";
                    req.CompletionTimestamp = DateTime.UtcNow;
                    await scopedDbContext.SaveChangesAsync(token);
                    _logger.LogInformation("Completed data anonymization for PersonId {PersonId}", personId);
                }
            });

            return new PrivacyRequestStatusResponse
            {
                Success = true,
                Message = "Your account deletion request has been received and will be processed shortly.",
                RequestId = privacyRequest.Id,
                Status = "Pending"
            };
        }

        /// <summary>
        /// Emails every account admin that a data-subject request is waiting. The privacy
        /// policy promises a person responds within 30 days, and nothing else surfaces these
        /// rows - without this the promise has no process behind it.
        /// Carries identifiers only, never the subject's data: the whole point of the
        /// accompanying export fix is that this material does not belong in transit or in logs.
        /// Notification failures are logged and never block the request itself.
        /// </summary>
        private async Task NotifyAdminsOfPrivacyRequestAsync(PrivacyRequestEntity request, long personId)
        {
            try
            {
                var adminUserIds = await _dbContext.UserClaims
                    .Where(c => c.ClaimType == AdminClaimType && c.ClaimValue == "true")
                    .Select(c => c.UserId)
                    .Distinct()
                    .ToListAsync();

                var admins = await _dbContext.Persons
                    .Where(p => p.UserId != null && adminUserIds.Contains(p.UserId))
                    .Select(p => new { p.Id, p.Email })
                    .ToListAsync();

                if (admins.Count == 0)
                {
                    _logger.LogWarning(
                        "Privacy request {RequestId} ({RequestType}) has no account admin to notify.",
                        request.Id, request.RequestType);
                    return;
                }

                var subject = $"NOM: {request.RequestType} request #{request.Id} needs a response";
                var body = $@"
<html><body>
<h2>Data-subject request</h2>
<p><strong>Type:</strong> {request.RequestType}</p>
<p><strong>Request ID:</strong> {request.Id}</p>
<p><strong>Person ID:</strong> {personId}</p>
<p><strong>Received:</strong> {request.RequestTimestamp:u}</p>
<p>The published privacy policy commits to responding within 30 days. Deletion is applied
automatically; an export has to be produced and sent by hand from the PrivacyRequests record.</p>
<p>No personal data is included in this message deliberately.</p>
</body></html>";

                foreach (var admin in admins.Where(a => !string.IsNullOrWhiteSpace(a.Email)))
                {
                    await _email.SendAsync(admin.Email!, subject, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to notify admins of privacy request {RequestId}", request.Id);
            }
        }

    }
}
