using Nom.Orch.Models.Plan;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IPlanOrchestrationService
    {
        Task<List<PlanModel>> GetCuratedPlansAsync();
        Task<List<PlanModel>> GetMyPlansAsync(long authorId);
        Task<PlanModel> GetPlanByIdAsync(long planId);
        Task<PlanModel> ClonePlanAsync(long sourcePlanId, long newAuthorId, string newPlanName);
        Task<PlanModel> CreatePlanAsync(CreatePlanRequest request, long authorId);
        Task UpdatePlanAsync(long planId, UpdatePlanRequest request, long authorId);
        Task DeletePlanAsync(long planId, long authorId);
        Task SubmitPlanForCurationAsync(long planId, long authorId);
    }
} 