// File: Nom.Orch/Interfaces/IDataExportOrchestrationService.cs

using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines the contract for a service that handles exporting user data.
    /// </summary>
    public interface IDataExportOrchestrationService
    {
        /// <summary>
        /// Gathers and exports a user's personal data.
        /// </summary>
        /// <param name="personId">The ID of the person whose data is to be exported.</param>
        /// <param name="format">The requested format for the export (e.g., "json").</param>
        Task ExportPersonDataAsync(long personId, string format);
    }
}
