// File: Nom.Orch/Interfaces/IShoppingListOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    public interface IShoppingListOrchestrationService
    {
        Task<List<ShoppingListResponseModel>> GetAllShoppingListsAsync();
        Task<ShoppingListCreateResponseModel> CreateShoppingListAsync(ShoppingListCreateModel model);
        Task<ShoppingListResponseModel?> GetShoppingListAsync(long id);
        Task<ShoppingListResponseModel?> UpdateShoppingListAsync(long id, ShoppingListUpdateModel model);
        Task<bool> DeleteShoppingListAsync(long id);
        Task<ShoppingListItemResponseModel> AddItemAsync(ShoppingListItemCreateModel model);
        Task<ShoppingListItemResponseModel?> UpdateItemAsync(long id, ShoppingListItemUpdateModel model);
        Task<bool> DeleteItemAsync(long id);

        // Recipe Integration
        Task<ShoppingListResponseModel> AddRecipeIngredientsAsync(ShoppingListRecipeAddModel model);
        Task<ShoppingListResponseModel> RemoveRecipeIngredientsAsync(ShoppingListRecipeRemoveModel model);
    }
} 