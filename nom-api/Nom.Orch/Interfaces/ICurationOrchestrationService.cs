using Nom.Orch.Models.Curation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface ICurationOrchestrationService
    {
        Task SubmitForCurationAsync(SubmitForCurationRequest request, long authorId);
        Task ApproveAsync(CurationDecisionRequest request, long adminId);
        Task RequestRevisionAsync(CurationDecisionRequest request, long adminId);
        Task RejectAsync(CurationDecisionRequest request, long adminId);
        Task<List<CurationQueueItemModel>> GetCurationQueueAsync();
    }
}