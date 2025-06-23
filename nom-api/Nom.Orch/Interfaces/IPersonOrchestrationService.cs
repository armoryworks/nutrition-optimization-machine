// Nom.Orch/Interfaces/IPersonOrchestrationService.cs
using System.Threading.Tasks;
using Nom.Data.Person;
using Nom.Orch.Models.Person; // Assuming PersonEntity is needed here

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines the business logic operations related to Person entities,
    /// especially for post-registration and initial setup.
    /// </summary>
    public interface IPersonOrchestrationService
    {
        /// <summary>
        /// Handles the creation and initial setup of a Person entity
        /// after a new user has successfully registered in the authentication system.
        /// </summary>
        /// <param name="identityUserId">The ID of the newly created IdentityUser.</param>
        /// <param name="personName">The initial name for the person.</param>
        /// <returns>The created PersonEntity.</returns>
        Task<PersonEntity> SetupNewRegisteredPersonAsync(string identityUserId, string personName);

        /// <summary>
        /// Generates a unique invitation code for a person.
        /// </summary>
        /// <returns>A unique invitation code string.</returns>
        Task<string> GenerateUniqueInvitationCodeAsync();

        Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request);
        // Add other person-related orchestration methods here as needed


        long GetCurrentPersonId();

    }
}