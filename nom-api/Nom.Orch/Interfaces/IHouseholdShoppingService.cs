using System.Threading.Tasks;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    public sealed class ShoppingItemAddResult
    {
        public HouseholdShoppingItemModel Item { get; init; } = new();
        public bool Created { get; init; }
    }

    /// <summary>
    /// The household's shared shopping state: the ad-hoc items members typed in, and the
    /// check-off marks on every line including the ones the plan and pantry compute.
    /// Membership is the caller's responsibility; the service trusts the household id it is given.
    /// </summary>
    public interface IHouseholdShoppingService
    {
        Task<ShoppingStateModel> GetStateAsync(long householdId);
        Task<ShoppingItemAddResult?> AddItemAsync(long householdId, long personId, HouseholdShoppingItemCreateModel model);
        Task<HouseholdShoppingItemModel?> GetItemAsync(long id);
        Task<HouseholdShoppingItemModel?> UpdateItemAsync(long id, long personId, HouseholdShoppingItemUpdateModel model);
        Task<bool> DeleteItemAsync(long id, long personId);
        Task<ShoppingCheckResultModel?> SetCheckAsync(long householdId, long personId, ShoppingCheckRequestModel request);
        Task ClearChecksAsync(long householdId, long personId);
    }
}
