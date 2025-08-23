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

        Task<PersonModel> GetPersonByUserIdAsync(string userId);
        Task<PersonModel> GetPersonByIdAsync(long personId);
        Task<List<PersonModel>> GetPersonsByPlanIdAsync(long planId);
        Task<PersonModel> UpdatePersonAsync(UpdatePersonRequest request);
        Task<bool> DeletePersonAsync(long personId);
        Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request);

        /// <summary>
        /// Gets the current onboarding state for a user, including existing person data
        /// </summary>
        /// <param name="userId">Optional user ID to fetch onboarding state for</param>
        /// <returns>The current onboarding state</returns>
        Task<OnboardingStateResponse> GetOnboardingStateAsync(string? userId = null);

        /// <summary>
        /// Gets the current PersonId from the authenticated user's claims.
        /// Returns null if the user is in registration phase and doesn't have a PersonId yet.
        /// </summary>
        long? GetCurrentPersonId();

        /// <summary>
        /// Gets the current PersonId from the authenticated user's claims.
        /// Throws UnauthorizedAccessException if PersonId is not available.
        /// Use this method only for endpoints that require a complete user profile.
        /// </summary>
        long GetCurrentPersonIdRequired();
    }
}