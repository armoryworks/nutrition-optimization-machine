// File: Nom.Orch/Interfaces/IShoppingListOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    public interface IShoppingListOrchestrationService
    {
        Task<List<ShoppingListResponseModel>> GetAllShoppingListsAsync(long personId);
        Task<ShoppingListCreateResponseModel> CreateShoppingListAsync(ShoppingListCreateModel model, long authorId);
        Task<ShoppingListResponseModel?> GetShoppingListAsync(long id, long personId);
        Task<ShoppingListResponseModel?> UpdateShoppingListAsync(long id, ShoppingListUpdateModel model, long personId);
        Task<bool> DeleteShoppingListAsync(long id, long personId);
        Task<ShoppingListItemResponseModel?> AddItemAsync(ShoppingListItemCreateModel model, long personId);
        Task<ShoppingListItemResponseModel?> UpdateItemAsync(long id, ShoppingListItemUpdateModel model, long personId);
        Task<bool> DeleteItemAsync(long id, long personId);

        // Recipe Integration
        Task<ShoppingListResponseModel> AddRecipeIngredientsAsync(ShoppingListRecipeAddModel model, long personId);
        Task<ShoppingListResponseModel> RemoveRecipeIngredientsAsync(ShoppingListRecipeRemoveModel model, long personId);
    }
} 