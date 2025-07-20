// File: Nom.Orch/Interfaces/IPersonOrchestrationService.cs
using System.Threading.Tasks;
using Nom.Orch.Models.Person;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines the business logic operations related to Person entities,
    /// especially for post-registration and initial setup.
    /// </summary>
    public interface IPersonOrchestrationService
    {
        /// <summary>
        /// Creates a new person if one does not already exist for the current user,
        /// otherwise updates the existing person's name. This prevents duplicate entries.
        /// </summary>
        /// <param name="request">The person data to create or update.</param>
        /// <returns>A response containing the ID of the created or updated person.</returns>
        Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request);

        /// <summary>
        /// Generates a unique invitation code for a person.
        /// </summary>
        /// <returns>A unique invitation code string.</returns>
        Task<string> GenerateUniqueInvitationCodeAsync();

        Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request);

        long GetCurrentPersonId();
    }
}