// File: Nom.Orch/Interfaces/IReferenceOrchestrationService.cs

using Nom.Orch.Models.Reference;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IReferenceOrchestrationService
    {
        /// <summary>
        /// Gets all references for a specific reference group.
        /// </summary>
        /// <param name="discriminatorId">The discriminator ID for the reference group</param>
        /// <returns>List of references for the specified group</returns>
        Task<List<Nom.Data.Reference.GroupedReferenceViewEntity>> GetReferencesByGroupAsync(long discriminatorId);

        /// <summary>
        /// Gets multiple reference groups in one call for performance.
        /// </summary>
        /// <param name="discriminatorIds">Array of discriminator IDs</param>
        /// <returns>Dictionary of discriminator ID to references mapping</returns>
        Task<Dictionary<long, List<Nom.Data.Reference.GroupedReferenceViewEntity>>> GetReferencesBulkAsync(long[] discriminatorIds);
    }
}