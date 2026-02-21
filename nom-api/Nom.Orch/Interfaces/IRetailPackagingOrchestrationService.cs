using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    public interface IRetailPackagingOrchestrationService
    {
        Task<List<RetailPackagingResponseModel>> GetAllAsync();
        Task<RetailPackagingResponseModel?> GetByIdAsync(long id);
        Task<RetailPackagingResponseModel> CreateAsync(RetailPackagingCreateModel model);
        Task<RetailPackagingResponseModel?> UpdateAsync(long id, RetailPackagingUpdateModel model);
        Task<bool> DeleteAsync(long id);
        Task<RetailPackagingLookupResponse> LookupPackagingAsync(List<string> ingredientNames, CancellationToken ct);
    }
}
