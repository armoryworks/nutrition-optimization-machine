// File: Nom.Orch/Services/PrivacyOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Privacy;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Privacy;
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

        public PrivacyOrchestrationService(
            ApplicationDbContext dbContext,
            IBackgroundTaskQueueOrchestrationService taskQueue,
            IServiceProvider serviceProvider,
            ILogger<PrivacyOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _taskQueue = taskQueue;
            _serviceProvider = serviceProvider;
            _logger = logger;
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
    }
}
