using Nom.Orch.Models.Pantry;

namespace Nom.Orch.Interfaces
{
    public interface IPantryOrchestrationService
    {
        Task<List<PantryItemResponseModel>> GetPantryItemsAsync(long householdId);
        Task<PantryItemResponseModel?> GetPantryItemAsync(long id);
        Task<PantryItemResponseModel> AddPantryItemAsync(PantryItemCreateModel model);
        Task<List<PantryItemResponseModel>> AddPantryItemsBatchAsync(List<PantryItemCreateModel> items);
        Task<PantryItemResponseModel?> UpdatePantryItemAsync(long id, PantryItemUpdateModel model);
        Task<bool> RemovePantryItemAsync(long id);
        Task<ShoppingNeedsResponseModel> GetShoppingNeedsAsync(long householdId, int daysAhead);
        Task<bool> DeductFromPantryAsync(long mealPlanId);
    }
}
