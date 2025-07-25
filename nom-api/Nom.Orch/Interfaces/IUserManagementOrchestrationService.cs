using Nom.Orch.Models.UserManagement;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IUserManagementOrchestrationService
    {
        Task UpdateUserClaimsAsync(UpdateUserClaimsRequest request);
    }
}