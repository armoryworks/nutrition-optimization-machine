using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    public interface IShoppingListCategoryOrchestrationService
    {
        Task<List<ShoppingListCategoryResponseModel>> GetAllCategoriesAsync();
        Task<ShoppingListCategoryResponseModel?> GetCategoryAsync(long id);
        Task<ShoppingListCategoryResponseModel> CreateCategoryAsync(ShoppingListCategoryCreateModel model);
        Task<ShoppingListCategoryResponseModel?> UpdateCategoryAsync(long id, ShoppingListCategoryCreateModel model);
        Task<bool> DeleteCategoryAsync(long id);
        Task<bool> MoveItemsToCategoryAsync(ShoppingListBulkOperationModel model);
    }
} 