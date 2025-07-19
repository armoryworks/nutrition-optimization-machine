// File: Nom.Orch/Interfaces/IAuditOrchestrationService.cs

using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines a service for creating audit log entries for data processing activities.
    /// </summary>
    public interface IAuditOrchestrationService
    {
        /// <summary>
        /// Creates a new audit log entry.
        /// </summary>
        /// <param name="personId">The ID of the person whose data is being affected.</param>
        /// <param name="actionType">The type of action being performed (e.g., "OnboardingComplete").</param>
        /// <param name="details">A description of the action.</param>
        Task LogAsync(long personId, string actionType, string details);
    }
}
