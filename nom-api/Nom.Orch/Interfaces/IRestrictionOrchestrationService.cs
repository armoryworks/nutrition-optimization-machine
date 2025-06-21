using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IRestrictionOrchestrationService
    {
        Task<long> GetRestrictionTypeRefIdByNameAsync(string restrictionTypeName);
        Task<List<string>> GetCuratedIngredientsAsync();
        Task<List<string>> GetMicronutrientsAsync();
        // Add other restriction-related orchestration methods here
    }
}
