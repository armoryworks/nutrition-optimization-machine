using Nom.Orch.Models.Label;

namespace Nom.Orch.Interfaces
{
    public interface ILabelOrchestrationService
    {
        Task<List<LabelResponseModel>> GetLabelsAsync();
        Task<long> CreateLabelAsync(LabelCreateModel model);
        Task<LabelResponseModel?> UpdateLabelAsync(long id, LabelCreateModel model);
        Task<bool> DeleteLabelAsync(long id);
    }
}
