// File: Nom.Orch/Interfaces/IPersonOrchestrationService.cs
using System.Threading.Tasks;
using Nom.Data.Person;
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
        Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request);

        /// <summary>
        /// Creates a Person entity for a newly registered user.
        /// </summary>
        Task<PersonEntity> SetupNewRegisteredPersonAsync(string identityUserId, string personName);

        Task<PersonModel> GetPersonByUserIdAsync(string userId);
        Task<PersonModel> GetPersonByIdAsync(long personId);
        Task<List<PersonModel>> GetAllPersonsAsync();
        Task<List<PersonModel>> GetPersonsForHouseholdsAsync(List<long> householdIds);
        Task<List<PersonModel>> GetPersonsByPlanIdAsync(long planId);
        Task<PersonModel> UpdatePersonAsync(UpdatePersonRequest request);
        Task<bool> DeletePersonAsync(long personId);
        Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request);

        /// <summary>
        /// Gets the onboarding state for a specific person by their ID.
        /// </summary>
        Task<OnboardingStateResponse> GetOnboardingStateAsync(long personId);

        /// <summary>
        /// Gets the current PersonId from the authenticated user's claims.
        /// Returns null if the user is in registration phase and doesn't have a PersonId yet.
        /// </summary>
        long? GetCurrentPersonId();

        /// <summary>
        /// Gets the current PersonId from the authenticated user's claims.
        /// Throws UnauthorizedAccessException if PersonId is not available.
        /// </summary>
        long GetCurrentPersonIdRequired();

        Task<List<PersonModel>> SearchPersonsAsync(string query, int limit = 20);

        /// <summary>
        /// Checks if a person is an active member of any of the specified households.
        /// </summary>
        Task<bool> IsPersonInHouseholdsAsync(long personId, List<long> householdIds);

        /// <summary>
        /// Saves a person's profile (name + attributes).
        /// Replaces all existing attributes.
        /// </summary>
        Task<PersonModel> SaveProfileAsync(long personId, SaveProfileRequest request);

        /// <summary>
        /// Saves person-level restrictions (no plan required).
        /// Replaces all existing person-level restrictions (where PlanId is null).
        /// </summary>
        Task SaveRestrictionsAsync(long personId, List<RestrictionRequest> restrictions);
    }
}