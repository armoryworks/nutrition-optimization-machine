// File: Nom.Orch/Interfaces/IClientAdminOrchestrationService.cs

using Nom.Orch.Models.UserManagement;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin portal reads over households ("clients") — the household-centric
    /// counterpart to user management. Read-only by design: mutations to
    /// households stay with the household owner flows.
    /// </summary>
    public interface IClientAdminOrchestrationService
    {
        Task<List<AdminHouseholdModel>> GetHouseholdsAsync();
        Task<List<AdminHouseholdMemberModel>> GetHouseholdMembersAsync(long householdId);
    }
}
