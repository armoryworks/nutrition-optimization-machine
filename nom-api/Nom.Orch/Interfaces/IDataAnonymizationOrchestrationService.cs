// File: Nom.Orch/Interfaces/IDataAnonymizationOrchestrationService.cs

using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines the contract for a service that handles the anonymization of user data.
    /// </summary>
    public interface IDataAnonymizationOrchestrationService
    {
        /// <summary>
        /// Anonymizes or deletes all personal data associated with a given Person ID.
        /// </summary>
        /// <param name="personId">The ID of the person to anonymize.</param>
        Task AnonymizePersonDataAsync(long personId);
    }
}
