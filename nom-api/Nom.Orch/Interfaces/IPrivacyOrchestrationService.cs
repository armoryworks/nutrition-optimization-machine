// File: Nom.Orch/Interfaces/IPrivacyOrchestrationService.cs

using Nom.Orch.Models.Privacy;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines the business logic operations for managing user privacy,
    /// consent, and data subject rights in compliance with GDPR.
    /// </summary>
    public interface IPrivacyOrchestrationService
    {
        /// <summary>
        /// Processes a user's request to update their consent settings.
        /// </summary>
        /// <param name="request">The request containing the consent updates.</param>
        /// <param name="personId">The ID of the person whose consent is being updated.</param>
        /// <returns>A response indicating the outcome of the operation.</returns>
        Task<bool> UpdateConsentAsync(UpdateConsentRequest request, long personId);

        /// <summary>
        /// Initiates a data export process for a user.
        /// </summary>
        /// <param name="request">The request specifying the export format.</param>
        /// <param name="personId">The ID of the person requesting the export.</param>
        /// <returns>A response confirming the request has been queued.</returns>
        Task<PrivacyRequestStatusResponse> RequestDataExportAsync(DataExportRequest request, long personId);

        /// <summary>
        /// Initiates a data deletion process for a user.
        /// </summary>
        /// <param name="request">The request confirming the deletion.</param>
        /// <param name="personId">The ID of the person requesting deletion.</param>
        /// <returns>A response confirming the deletion request has been queued.</returns>
        Task<PrivacyRequestStatusResponse> RequestDataDeletionAsync(DataDeletionRequest request, long personId);
    }
}
