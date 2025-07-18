// File: Nom.Orch/Services/PrivacyOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Privacy;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Privacy;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the business logic for managing user privacy, consent,
    /// and data subject rights in compliance with GDPR.
    /// </summary>
    public class PrivacyOrchestrationService : IPrivacyOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;

        public PrivacyOrchestrationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Processes a user's request to update their consent settings. It finds or creates
        /// consent records for the specified person and updates their status.
        /// </summary>
        /// <param name="request">The request containing the consent updates.</param>
        /// <param name="personId">The ID of the person whose consent is being updated.</param>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        public async Task<bool> UpdateConsentAsync(UpdateConsentRequest request, long personId)
        {
            var consentTypeRefIds = request.Consents.Select(c => c.ConsentTypeRefId).ToList();

            // Fetch the names of the consent types from the reference table
            var consentTypes = await _dbContext.References
                .Where(r => consentTypeRefIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name);

            // Fetch all existing consent records for this user to avoid multiple queries
            var existingUserConsents = await _dbContext.UserConsents
                .Where(uc => uc.PersonId == personId)
                .ToListAsync();

            foreach (var consentRequest in request.Consents)
            {
                if (!consentTypes.TryGetValue(consentRequest.ConsentTypeRefId, out var consentTypeName))
                {
                    // Log or handle the case where an invalid ConsentTypeRefId is provided
                    continue;
                }

                var existingConsent = existingUserConsents.FirstOrDefault(uc => uc.ConsentType == consentTypeName);

                if (existingConsent == null)
                {
                    // This is the first time this user is setting this specific consent
                    _dbContext.UserConsents.Add(new UserConsentEntity
                    {
                        PersonId = personId,
                        ConsentType = consentTypeName,
                        IsConsented = consentRequest.IsConsented,
                        ConsentTimestamp = DateTime.UtcNow,
                        ConsentVersion = "1.0", // Placeholder for versioning logic
                        LegalBasis = "Consent"
                    });
                }
                else
                {
                    // User is updating an existing consent preference
                    existingConsent.IsConsented = consentRequest.IsConsented;
                    existingConsent.ConsentTimestamp = DateTime.UtcNow;
                    _dbContext.UserConsents.Update(existingConsent);
                }
            }

            await _dbContext.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Creates a record to track a user's data export request.
        /// In a real system, this would trigger a background job.
        /// </summary>
        /// <param name="request">The request specifying the export format.</param>
        /// <param name="personId">The ID of the person requesting the export.</param>
        /// <returns>A response confirming the request has been queued.</returns>
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

            return new PrivacyRequestStatusResponse
            {
                Success = true,
                Message = "Your data export request has been received and is being processed.",
                RequestId = Guid.NewGuid(), // A unique tracking ID for the user
                Status = "Pending"
            };
        }

        /// <summary>
        /// Creates a record to track a user's data deletion request.
        /// This would trigger a background job to anonymize or delete user data.
        /// </summary>
        /// <param name="request">The request confirming the deletion.</param>
        /// <param name="personId">The ID of the person requesting deletion.</param>
        /// <returns>A response confirming the deletion request has been queued.</returns>
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

            return new PrivacyRequestStatusResponse
            {
                Success = true,
                Message = "Your account deletion request has been received and will be processed shortly.",
                RequestId = Guid.NewGuid(), // A unique tracking ID for the user
                Status = "Pending"
            };
        }
    }
}
